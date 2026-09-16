using Equibles.Core.AutoWiring;
using Equibles.DelayedTrades.BusinessLogic.Bars;
using Equibles.DelayedTrades.BusinessLogic.Configuration;
using Equibles.DelayedTrades.BusinessLogic.Listings;
using Equibles.DelayedTrades.BusinessLogic.Prints;
using Equibles.DelayedTrades.BusinessLogic.Schedule;
using Equibles.DelayedTrades.BusinessLogic.Sessions;
using Equibles.DelayedTrades.Data.Models;
using Equibles.DelayedTrades.Repositories;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.DelayedTrades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.DelayedTrades.BusinessLogic.Import;

// One market, one fetch: the current session feeds LatestDelayedTrade, the previous session feeds the price store.
[Service]
public class DelayedTradeImportService(
    IServiceScopeFactory scopeFactory,
    DelayedTradeBarWriter barWriter,
    IOptions<DelayedTradeScraperOptions> options,
    ILogger<DelayedTradeImportService> logger
)
{
    public async Task<DelayedTradeIntradayResult> PollIntraday(
        EquityMarket market,
        IDelayedTradeSource source,
        DateTime utcNow,
        CancellationToken cancellationToken
    )
    {
        var zone = DelayedTradeClock.Zone(market);
        var file = await source.Fetch(
            market.DelayedTradeLocationCode,
            DelayedTradeWindow.CurrentSession,
            cancellationToken
        );
        if (file.Outcome != DelayedTradeFetchOutcome.Served)
        {
            await RecordCapture(market, file, 0, null, cancellationToken);
            return new DelayedTradeIntradayResult(
                DelayedTradeIntradayOutcome.NoSession,
                null,
                0,
                0,
                0,
                0,
                0
            );
        }

        var counters = new DelayedTradeParseCounters();
        var ledger = DelayedTradeModificationLedger.Build(source.Parse(file, counters));
        var map = await LoadListings(market, cancellationToken);
        var delay = TimeSpan.FromMinutes(Math.Max(0, options.Value.PublicationDelayMinutes));
        var droppedAsFresh = 0;
        var unmatched = new HashSet<(string, string)>();
        // The second pass streams into the aggregator, so a million-print file is never held as a list;
        // the counters it fills are complete once Aggregate returns.
        IEnumerable<DelayedTradePrint> Matched()
        {
            foreach (var print in source.Parse(file, new DelayedTradeParseCounters()))
            {
                if (!ledger.IsEffective(print) || !DelayedTradePrintFilter.IsCounted(print))
                    continue;
                if (!DelayedTradePrintFilter.IsDelayed(print, file.FetchedAtUtc, delay))
                {
                    droppedAsFresh++;
                    continue;
                }
                if (!map.TryResolve(print.Isin, print.Venue, out var listing))
                {
                    unmatched.Add((print.Isin, print.Venue));
                    continue;
                }
                if (
                    !DelayedTradePrintFilter.MatchesQuotation(
                        print,
                        listing.TradingCurrency,
                        listing.QuoteUnitMultiplier
                    )
                )
                    continue;
                yield return print;
            }
        }
        var aggregation = DelayedTradeSessionAggregator.Aggregate(
            Matched(),
            zone,
            options.Value.DetectDuplicateTradeIds
        );
        var sessionDate = SessionDateOf(aggregation);
        var local = DelayedTradeClock.Local(utcNow, zone);
        // Complete once the closing auction's own prints have aged past the delay the poll enforces.
        var complete =
            sessionDate != null
            && (
                sessionDate < DateOnly.FromDateTime(local)
                || TimeOnly.FromDateTime(local)
                    > DelayedTradeSchedule.CompletionTime(market, options.Value)
            );
        var written = await UpsertLatestTrades(
            market,
            source,
            file,
            map,
            aggregation,
            sessionDate,
            complete,
            cancellationToken
        );
        await RecordCapture(market, file, counters.Rows, sessionDate, cancellationToken);
        var matchedListings = aggregation.Bars.Count(bar => bar.HasBar);
        logger.LogInformation(
            "{Market} intraday: {Rows} rows, {Matched} listings updated, {Unmatched} unmatched ISIN/venue keys, {Fresh} prints younger than the delay",
            market.Code,
            counters.Rows,
            written,
            unmatched.Count,
            droppedAsFresh
        );
        return new DelayedTradeIntradayResult(
            matchedListings == 0
                ? DelayedTradeIntradayOutcome.NothingMatched
                : DelayedTradeIntradayOutcome.Updated,
            sessionDate,
            counters.Rows,
            matchedListings,
            unmatched.Count,
            droppedAsFresh,
            written
        );
    }

    public async Task<DelayedTradeSettleResult> SettleSession(
        EquityMarket market,
        IDelayedTradeSource source,
        bool rederive,
        DateTime utcNow,
        CancellationToken cancellationToken
    )
    {
        var zone = DelayedTradeClock.Zone(market);
        var file = await source.Fetch(
            market.DelayedTradeLocationCode,
            DelayedTradeWindow.PreviousSession,
            cancellationToken
        );
        if (file.Outcome != DelayedTradeFetchOutcome.Served)
        {
            await RecordCapture(market, file, 0, null, cancellationToken);
            return new DelayedTradeSettleResult(
                DelayedTradeSettleOutcome.NoSession,
                null,
                null,
                null
            );
        }

        if (!rederive)
        {
            // The venue still serves the file the latest marker was derived from: nothing new to parse.
            var latest = await LatestMarker(market, cancellationToken);
            if (latest != null && latest.FileSha256 == file.Sha256)
            {
                await RecordCapture(
                    market,
                    file,
                    await RowsOfFile(market, file, cancellationToken),
                    latest.PartitionDate,
                    cancellationToken
                );
                return new DelayedTradeSettleResult(
                    DelayedTradeSettleOutcome.AlreadyImported,
                    latest.PartitionDate,
                    latest,
                    null
                );
            }
        }

        var counters = new DelayedTradeParseCounters();
        var ledger = DelayedTradeModificationLedger.Build(source.Parse(file, counters));
        var map = await LoadListings(market, cancellationToken);
        var partition = new DelayedTradeImportPartition
        {
            Dataset = DelayedTradeDataset.SettledBars,
            ScopeKey = market.Code,
            SourceUrl = file.SourceUrl,
            TermsUrl = file.TermsUrl,
            FileSha256 = file.Sha256,
            FileBytes = file.Bytes,
            CancelledCount = ledger.CancelledCount,
            AmendedCount = ledger.AmendedCount,
        };
        var unmatched = new HashSet<(string, string)>();
        var ambiguous = new HashSet<(string, string)>();
        var isins = new HashSet<string>(StringComparer.Ordinal);
        // Streams into the aggregator like the intraday pass; the sets and counters are complete once it returns.
        IEnumerable<DelayedTradePrint> Matched()
        {
            foreach (var print in source.Parse(file, new DelayedTradeParseCounters()))
            {
                if (!ledger.IsEffective(print) || !DelayedTradePrintFilter.IsCounted(print))
                    continue;
                isins.Add(print.Isin);
                if (!map.TryResolve(print.Isin, print.Venue, out var listing))
                {
                    if (map.IsAmbiguous(print.Isin, print.Venue))
                        ambiguous.Add((print.Isin, print.Venue));
                    else
                        unmatched.Add((print.Isin, print.Venue));
                    continue;
                }
                if (
                    !DelayedTradePrintFilter.MatchesQuotation(
                        print,
                        listing.TradingCurrency,
                        listing.QuoteUnitMultiplier
                    )
                )
                {
                    partition.CurrencyMismatchCount++;
                    continue;
                }
                yield return print;
            }
        }
        var aggregation = DelayedTradeSessionAggregator.Aggregate(
            Matched(),
            zone,
            options.Value.DetectDuplicateTradeIds
        );
        partition.PrintCount = aggregation.PrintCount;
        partition.LitPrintCount = aggregation.LitPrintCount;
        partition.DarkPrintCount = aggregation.DarkPrintCount;
        partition.DuplicateCount = aggregation.DuplicateCount;
        partition.IsinCount = isins.Count;
        partition.UnmatchedCount = unmatched.Count;
        partition.AmbiguousCount = ambiguous.Count;

        var sessionDate = SessionDateOf(aggregation);
        await RecordCapture(market, file, counters.Rows, sessionDate, cancellationToken);
        if (sessionDate == null)
        {
            // A file that matches no verified listing is a broken map or an empty market, never a settled session.
            logger.LogError(
                "{Market} settle: {Rows} rows and no print matched a verified listing; the session stays unmarked",
                market.Code,
                counters.Rows
            );
            return new DelayedTradeSettleResult(
                DelayedTradeSettleOutcome.Refused,
                null,
                partition,
                "no print matched a verified listing"
            );
        }
        partition.PartitionDate = sessionDate.Value;

        DelayedTradeImportPartition existing;
        using (var scope = scopeFactory.CreateScope())
        {
            existing = await scope
                .ServiceProvider.GetRequiredService<DelayedTradeImportPartitionRepository>()
                .GetMarker(
                    DelayedTradeDataset.SettledBars,
                    sessionDate.Value,
                    market.Code,
                    cancellationToken
                );
        }
        if (existing != null && !rederive)
            return new DelayedTradeSettleResult(
                DelayedTradeSettleOutcome.AlreadyImported,
                sessionDate,
                existing,
                null
            );

        var localToday = DelayedTradeClock.LocalDate(utcNow, zone);
        var currencyToken = market.Currency;
        var failures = 0;
        var unchanged = 0;
        foreach (var bar in aggregation.Bars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bar.SessionDate != sessionDate)
            {
                partition.OutOfSessionCount += bar.PrintCount;
                continue;
            }
            if (!bar.HasBar)
            {
                partition.BarsSkippedInvalid++;
                continue;
            }
            if (!map.TryResolve(bar.Isin, bar.Venue, out var listing))
                continue;
            partition.MatchedCount++;
            try
            {
                var token = bar.LastPriceForming?.Currency ?? currencyToken;
                var outcome = await barWriter.Write(
                    listing,
                    bar,
                    token,
                    localToday,
                    cancellationToken
                );
                if (outcome == DelayedTradeBarOutcome.Unchanged)
                    unchanged++;
                else
                    Count(partition, outcome);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures++;
                logger.LogWarning(
                    exception,
                    "{Market} settle: bar write failed for {Isin} on {Venue} {Date}",
                    market.Code,
                    bar.Isin,
                    bar.Venue,
                    bar.SessionDate
                );
            }
        }

        if (partition.MatchedCount == 0)
            return new DelayedTradeSettleResult(
                DelayedTradeSettleOutcome.Refused,
                sessionDate,
                partition,
                "no bar matched a verified listing"
            );
        // A session no bar of which reached the store is not settled: unsettled or skipped bars are counted, never marked.
        var landed =
            partition.BarsInserted
            + partition.BarsOverwroteYahoo
            + partition.BarsRederived
            + unchanged;
        if (landed == 0)
        {
            var refusal =
                $"no bar landed: {partition.BarsUnsettled} unsettled, {partition.BarsSkippedIdentity} identity skips, {partition.BarsSkippedBasis} basis skips, {partition.BarsSkippedInvalid} invalid";
            logger.LogWarning(
                "{Market} settle {Date}: {Refusal}; the session stays unmarked",
                market.Code,
                sessionDate,
                refusal
            );
            return new DelayedTradeSettleResult(
                DelayedTradeSettleOutcome.Refused,
                sessionDate,
                partition,
                refusal
            );
        }
        if (failures > 0)
        {
            logger.LogWarning(
                "{Market} settle {Date}: {Failures} bar writes failed; the session stays retryable",
                market.Code,
                sessionDate,
                failures
            );
            return new DelayedTradeSettleResult(
                DelayedTradeSettleOutcome.Refused,
                sessionDate,
                partition,
                $"{failures} bar writes failed"
            );
        }

        var marker = await MarkImported(partition, existing, cancellationToken);
        logger.LogInformation(
            "{Market} settle {Date}: {Prints} prints, {Matched} listings, {Inserted} inserted, {Overwrote} replaced feed bars, {Rederived} re-derived, {Basis} basis skips, {Unmatched} unmatched keys",
            market.Code,
            sessionDate,
            partition.PrintCount,
            partition.MatchedCount,
            partition.BarsInserted,
            partition.BarsOverwroteYahoo,
            partition.BarsRederived,
            partition.BarsSkippedBasis,
            partition.UnmatchedCount
        );
        return new DelayedTradeSettleResult(
            existing == null
                ? DelayedTradeSettleOutcome.Imported
                : DelayedTradeSettleOutcome.Rederived,
            sessionDate,
            marker,
            null
        );
    }

    public async Task<int> PruneCaptures(DateTime utcNow, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var captures =
            scope.ServiceProvider.GetRequiredService<DelayedTradeFileCaptureRepository>();
        return await captures.PruneBefore(
            utcNow.AddDays(-Math.Max(1, options.Value.CaptureRetentionDays)),
            cancellationToken
        );
    }

    // The session a file describes is the date most of its counted prints fall on in the market's zone.
    internal static DateOnly? SessionDateOf(DelayedTradeSessionAggregation aggregation) =>
        aggregation
            .Bars.Where(bar => bar.HasBar)
            .GroupBy(bar => bar.SessionDate)
            .OrderByDescending(group => group.Sum(bar => bar.PrintCount))
            .ThenByDescending(group => group.Key)
            .Select(group => (DateOnly?)group.Key)
            .FirstOrDefault();

    private static void Count(DelayedTradeImportPartition partition, DelayedTradeBarOutcome outcome)
    {
        switch (outcome)
        {
            case DelayedTradeBarOutcome.Inserted:
                partition.BarsInserted++;
                break;
            case DelayedTradeBarOutcome.OverwroteYahoo:
                partition.BarsOverwroteYahoo++;
                break;
            case DelayedTradeBarOutcome.Rederived:
                partition.BarsRederived++;
                break;
            case DelayedTradeBarOutcome.SkippedBasis:
                partition.BarsSkippedBasis++;
                break;
            case DelayedTradeBarOutcome.SkippedInvalid:
                partition.BarsSkippedInvalid++;
                break;
            case DelayedTradeBarOutcome.SkippedIdentity:
                partition.BarsSkippedIdentity++;
                break;
            case DelayedTradeBarOutcome.Unsettled:
                partition.BarsUnsettled++;
                break;
        }
    }

    private async Task<DelayedTradeImportPartition> LatestMarker(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        using var scope = scopeFactory.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<DelayedTradeImportPartitionRepository>()
            .GetLatestMarker(DelayedTradeDataset.SettledBars, market.Code, cancellationToken);
    }

    // The row count of a file already parsed once, so the ledger never records a served file as empty.
    private async Task<int> RowsOfFile(
        EquityMarket market,
        DelayedTradeFile file,
        CancellationToken cancellationToken
    )
    {
        using var scope = scopeFactory.CreateScope();
        return await scope
                .ServiceProvider.GetRequiredService<DelayedTradeFileCaptureRepository>()
                .GetRowsOfFile(market.Code, file.Sha256, cancellationToken)
            ?? 0;
    }

    private async Task<DelayedTradeListingMap> LoadListings(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<LatestDelayedTradeRepository>();
        var listings = await repository
            .GetVerifiedListings(market.MarketIdentifierCodes)
            .ToListAsync(cancellationToken);
        return DelayedTradeListingMap.Build(listings);
    }

    private async Task<int> UpsertLatestTrades(
        EquityMarket market,
        IDelayedTradeSource source,
        DelayedTradeFile file,
        DelayedTradeListingMap map,
        DelayedTradeSessionAggregation aggregation,
        DateOnly? sessionDate,
        bool complete,
        CancellationToken cancellationToken
    )
    {
        if (sessionDate == null)
            return 0;
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<LatestDelayedTradeRepository>();
        var existing = await repository
            .GetByMarket(market.Code)
            .ToDictionaryAsync(row => row.EquityListingId, cancellationToken);
        var written = 0;
        foreach (var bar in aggregation.Bars)
        {
            if (
                !bar.HasBar
                || bar.SessionDate != sessionDate
                || !map.TryResolve(bar.Isin, bar.Venue, out var listing)
            )
                continue;
            var last = bar.LastPriceForming;
            if (!existing.TryGetValue(listing.EquityListingId, out var row))
            {
                row = new LatestDelayedTrade { EquityListingId = listing.EquityListingId };
                repository.Add(row);
            }
            else if (row.SessionDate > bar.SessionDate)
                continue;
            row.Isin = bar.Isin;
            row.Mic = bar.Venue;
            row.Currency = listing.TradingCurrency;
            row.SourceKey = source.SourceKey;
            row.MarketCode = market.Code;
            row.SessionDate = bar.SessionDate;
            row.LastPrice = bar.Close.Value;
            row.LastQuantity = Math.Round(last.Quantity, 4, MidpointRounding.AwayFromZero);
            row.LastTradedAtUtc = last.TradedAtUtc;
            row.LastPublishedAtUtc = last.PublishedAtUtc;
            row.LastTradeId = Truncate(last.TradeId, 64);
            row.SessionOpen = bar.Open.Value;
            row.SessionHigh = bar.High.Value;
            row.SessionLow = bar.Low.Value;
            row.Volume = bar.Volume;
            row.LitVolume = bar.LitVolume;
            row.PrintCount = bar.PrintCount;
            row.IsSessionComplete = complete;
            row.SourceUrl = Truncate(file.SourceUrl, 500);
            row.TermsUrl = Truncate(file.TermsUrl, 500);
            row.FileSha256 = file.Sha256;
            row.CapturedAtUtc = file.FetchedAtUtc;
            row.UpdatedAt = DateTime.UtcNow;
            written++;
        }
        await repository.SaveChanges();
        return written;
    }

    private async Task<DelayedTradeImportPartition> MarkImported(
        DelayedTradeImportPartition partition,
        DelayedTradeImportPartition existing,
        CancellationToken cancellationToken
    )
    {
        using var scope = scopeFactory.CreateScope();
        var repository =
            scope.ServiceProvider.GetRequiredService<DelayedTradeImportPartitionRepository>();
        var marker = await repository.GetMarker(
            partition.Dataset,
            partition.PartitionDate,
            partition.ScopeKey,
            cancellationToken
        );
        if (marker == null)
        {
            marker = partition;
            marker.ImportedAt = DateTime.UtcNow;
            repository.Add(marker);
        }
        else
        {
            repository.Update(marker);
            Copy(partition, marker);
            marker.ImportedAt = DateTime.UtcNow;
            marker.RederivedCount++;
        }
        await repository.SaveChanges();
        return marker;
    }

    private static void Copy(DelayedTradeImportPartition from, DelayedTradeImportPartition to)
    {
        to.SourceUrl = from.SourceUrl;
        to.TermsUrl = from.TermsUrl;
        to.FileSha256 = from.FileSha256;
        to.FileBytes = from.FileBytes;
        to.PrintCount = from.PrintCount;
        to.LitPrintCount = from.LitPrintCount;
        to.DarkPrintCount = from.DarkPrintCount;
        to.CancelledCount = from.CancelledCount;
        to.AmendedCount = from.AmendedCount;
        to.DuplicateCount = from.DuplicateCount;
        to.OutOfSessionCount = from.OutOfSessionCount;
        to.IsinCount = from.IsinCount;
        to.MatchedCount = from.MatchedCount;
        to.UnmatchedCount = from.UnmatchedCount;
        to.AmbiguousCount = from.AmbiguousCount;
        to.CurrencyMismatchCount = from.CurrencyMismatchCount;
        to.BarsInserted = from.BarsInserted;
        to.BarsOverwroteYahoo = from.BarsOverwroteYahoo;
        to.BarsRederived = from.BarsRederived;
        to.BarsSkippedBasis = from.BarsSkippedBasis;
        to.BarsSkippedInvalid = from.BarsSkippedInvalid;
        to.BarsSkippedIdentity = from.BarsSkippedIdentity;
        to.BarsUnsettled = from.BarsUnsettled;
    }

    private async Task RecordCapture(
        EquityMarket market,
        DelayedTradeFile file,
        int rows,
        DateOnly? sessionDate,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopeFactory.CreateScope();
        var captures =
            scope.ServiceProvider.GetRequiredService<DelayedTradeFileCaptureRepository>();
        captures.Add(
            new DelayedTradeFileCapture
            {
                SourceKey = file.SourceKey,
                LocationCode = file.LocationCode,
                MarketCode = market.Code,
                Window = file.Window,
                Outcome = file.Outcome,
                SourceUrl = Truncate(file.SourceUrl, 500),
                TermsUrl = Truncate(file.TermsUrl, 500),
                Sha256 = file.Sha256,
                Bytes = file.Bytes,
                Rows = rows,
                SessionDate = sessionDate,
                FetchedAtUtc = file.FetchedAtUtc,
            }
        );
        await captures.SaveChanges();
    }

    private static string Truncate(string value, int length) =>
        value is { Length: > 0 } && value.Length > length ? value[..length] : value;
}
