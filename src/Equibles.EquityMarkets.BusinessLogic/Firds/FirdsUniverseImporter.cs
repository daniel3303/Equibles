using System.Globalization;
using Equibles.Core.AutoWiring;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.EquityMarkets.Repositories;
using Equibles.Integrations.Esma;
using Equibles.Integrations.Esma.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.EquityMarkets.BusinessLogic.Firds;

// Keeps the FIRDS universe current per authority: shares, plus exchange-traded funds, commodities and notes on venues the
// catalog covers. Each CFI category's newest complete full set replaces that category's rows, then every delta published
// since is applied in order. Files are never re-read.
[Service]
public class FirdsUniverseImporter(
    IEnumerable<IFirdsFileIndex> indexes,
    IServiceScopeFactory scopeFactory,
    ILogger<FirdsUniverseImporter> logger
) : IImporter
{
    // Npgsql binds at most 65,535 parameters per statement and every row binds fifteen, so a batch stays well below.
    private const int BatchSize = 1_000;
    private static readonly TimeSpan StaleDownloadAge = TimeSpan.FromDays(1);
    private static readonly TimeSpan IndexLookback = TimeSpan.FromDays(14);

    public async Task Import(CancellationToken cancellationToken)
    {
        FirdsDownloader.SweepStaleFiles(StaleDownloadAge);
        foreach (var index in indexes)
        {
            try
            {
                await ImportAuthority(index, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "FIRDS import for {Authority} failed; its stored universe is unchanged",
                    index.Authority
                );
            }
        }
    }

    private async Task ImportAuthority(IFirdsFileIndex index, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<FirdsImportRunRepository>();
        var records = scope.ServiceProvider.GetRequiredService<FirdsInstrumentRecordRepository>();
        var latestFull = new Dictionary<char, DateOnly?>();
        foreach (var category in Categories)
            latestFull[category] = await runs.GetLatestFullPublication(
                index.Authority,
                category,
                cancellationToken
            );
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // A category with no full set yet looks back far enough to find its first one.
        var since = Categories.Min(category =>
            latestFull[category] ?? today.AddDays(-IndexLookback.Days)
        );
        var files = await index.ListEquityFiles(since, cancellationToken);
        foreach (var category in Categories)
        {
            var newest = files
                .Where(file =>
                    file.FileType == FirdsFileType.Full
                    && FirdsFileNames.FullCategory(file.FileName) == category
                )
                .GroupBy(file => file.PublishedOn)
                .Where(set => IsCompleteSet(index.Authority, set.Key, set.ToList()))
                .OrderByDescending(set => set.Key)
                .FirstOrDefault();
            if (newest == null && latestFull[category] == null)
            {
                logger.LogWarning(
                    "{Authority} published no complete {Category} full set in the last {Days} days",
                    index.Authority,
                    category,
                    IndexLookback.Days
                );
                continue;
            }
            if (
                newest != null
                && (latestFull[category] == null || newest.Key > latestFull[category])
            )
            {
                try
                {
                    await ImportFullSet(
                        index,
                        runs,
                        records,
                        category,
                        newest.ToList(),
                        cancellationToken
                    );
                    latestFull[category] = newest.Key;
                }
                // A broken fund set must not stall the share deltas; it records no run, so the next pass retries it.
                catch (Exception exception)
                    when (category != 'E' && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(
                        exception,
                        "{Authority} full {Category} set {PublishedOn} failed; shares keep their deltas",
                        index.Authority,
                        category,
                        newest.Key
                    );
                }
            }
        }
        // Shares drive the delta walk, as before; a delta older than a category's full set must not rewind that category.
        if (latestFull['E'] is not { } sharesFrom)
            return;
        foreach (
            var delta in files
                .Where(file =>
                    file.FileType == FirdsFileType.Delta && file.PublishedOn >= sharesFrom
                )
                .OrderBy(file => file.PublishedOn)
                .ThenBy(file => file.FileName)
        )
        {
            if (await runs.Exists(index.Authority, delta.FileName, cancellationToken))
                continue;
            var run = await ImportFile(
                index,
                records,
                delta,
                category => latestFull[category] is { } from && delta.PublishedOn >= from,
                cancellationToken
            );
            runs.Add(run);
            await runs.SaveChanges();
        }
    }

    // Parts are named NNofMM; a set is complete only when every part is listed once and all agree on the total.
    private bool IsCompleteSet(string authority, DateOnly publishedOn, List<FirdsFile> parts)
    {
        var totals = parts
            .Select(part => int.Parse(part.FileName[^6..^4], CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        var names = parts.Select(part => part.FileName).Distinct().Count();
        if (totals.Count == 1 && totals[0] > 0 && names == parts.Count && parts.Count == totals[0])
            return true;
        if (totals.Count == 1 && names == parts.Count && parts.Count < totals[0])
            logger.LogDebug(
                "{Authority} full set {PublishedOn} lists {Parts} of {Expected} parts so far",
                authority,
                publishedOn,
                parts.Count,
                totals[0]
            );
        else
            logger.LogWarning(
                "{Authority} full set {PublishedOn} is inconsistent ({Parts} entries, totals {Totals}) and is ignored until the index is coherent",
                authority,
                publishedOn,
                parts.Count,
                string.Join(",", totals)
            );
        return false;
    }

    private async Task ImportFullSet(
        IFirdsFileIndex index,
        FirdsImportRunRepository runs,
        FirdsInstrumentRecordRepository records,
        char category,
        List<FirdsFile> parts,
        CancellationToken cancellationToken
    )
    {
        var startedAt = DateTime.UtcNow;
        var imported = new List<FirdsImportRun>();
        foreach (var part in parts.OrderBy(part => part.FileName))
            imported.Add(
                await ImportFile(
                    index,
                    records,
                    part,
                    recordCategory => recordCategory == category,
                    cancellationToken
                )
            );
        var removed = await records.MarkRemovedBefore(
            index.Authority,
            category,
            startedAt,
            DateTime.UtcNow,
            cancellationToken
        );
        // Runs are recorded only once the whole set and its sweep landed, so a retry re-reads every part.
        foreach (var run in imported)
            runs.Add(run);
        await runs.SaveChanges();
        logger.LogInformation(
            "{Authority} full {Category} set {PublishedOn} imported: {Stored} rows stored across {Parts} parts, {Removed} rows retired",
            index.Authority,
            category,
            parts[0].PublishedOn,
            imported.Sum(run => run.RowsStored),
            parts.Count,
            removed
        );
    }

    private async Task<FirdsImportRun> ImportFile(
        IFirdsFileIndex index,
        FirdsInstrumentRecordRepository records,
        FirdsFile file,
        Func<char, bool> admitsCategory,
        CancellationToken cancellationToken
    )
    {
        using var download = await index.Download(file, cancellationToken);
        var observedAt = DateTime.UtcNow;
        var read = 0;
        var stored = 0;
        var batch = new Dictionary<(string Isin, string Mic), FirdsInstrumentRecord>();
        await using (var xml = download.OpenXml())
        {
            await foreach (var record in FirdsRecordReader.Read(xml, cancellationToken))
            {
                read++;
                if (!IsStored(record) || !admitsCategory(record.Cfi[0]))
                    continue;
                batch[(record.Isin, record.Mic)] = Map(index.Authority, record, observedAt);
                if (batch.Count >= BatchSize)
                {
                    await records.UpsertRange(batch.Values, cancellationToken);
                    stored += batch.Count;
                    batch.Clear();
                }
            }
        }
        if (batch.Count > 0)
        {
            await records.UpsertRange(batch.Values, cancellationToken);
            stored += batch.Count;
        }
        logger.LogInformation(
            "{Authority} {FileName}: {Read} records read, {Stored} rows stored",
            index.Authority,
            file.FileName,
            read,
            stored
        );
        return new FirdsImportRun
        {
            Authority = index.Authority,
            Kind = file.FileType == FirdsFileType.Full ? FirdsFileKind.Full : FirdsFileKind.Delta,
            FileName = file.FileName,
            PublishedOn = file.PublishedOn,
            Checksum = file.Checksum,
            ImportedAt = DateTime.UtcNow,
            RowsRead = read,
            RowsStored = stored,
        };
    }

    private static readonly char[] Categories = ['E', 'C'];

    // Exchange-traded products are kept only on venues a catalog market reads: the register holds millions of structured
    // products under equity CFI codes elsewhere.
    private static readonly HashSet<string> CoveredVenues = EquityMarketCatalog
        .All.SelectMany(market => market.FirdsVenueCodes)
        .ToHashSet(StringComparer.Ordinal);

    internal static bool IsStored(FirdsRecord record) =>
        IsEquity(record.Cfi)
        || IsExchangeTradedProduct(record.Cfi) && CoveredVenues.Contains(record.Mic ?? "");

    // CFI category E is equities; group Y is structured products sold under equity ISINs, not ownership.
    internal static bool IsEquity(string cfi) =>
        cfi is { Length: 6 } && cfi[0] == 'E' && cfi[1] != 'Y';

    // Group CE is exchange-traded funds; EY holds the exchange-traded commodities and notes a venue lists as products.
    internal static bool IsExchangeTradedProduct(string cfi) =>
        cfi is { Length: 6 }
        && (
            cfi.StartsWith("CE", StringComparison.Ordinal)
            || cfi.StartsWith("EY", StringComparison.Ordinal)
        );

    private static FirdsInstrumentRecord Map(
        string authority,
        FirdsRecord record,
        DateTime observedAt
    ) =>
        new()
        {
            Authority = authority,
            Isin = record.Isin,
            Mic = record.Mic,
            IssuerRequested = record.IssuerRequested,
            Lei = record.Lei,
            Cfi = record.Cfi,
            Currency = Clip(record.Currency, 3),
            FullName = Clip(record.FullName, 350),
            ShortName = Clip(record.ShortName, 35),
            FirstTradeDate = record.FirstTradeDate,
            TerminationDate = record.TerminationDate,
            RelevantCompetentAuthority = Clip(record.RelevantCompetentAuthority, 2),
            RelevantTradingVenue = Clip(record.RelevantTradingVenue, 4),
            ObservedAt = observedAt,
            // A termination the delta reports without a date leaves the live universe from this observation on.
            RemovedAt =
                record.Kind == FirdsRecordKind.Terminated && record.TerminationDate == null
                    ? observedAt
                    : null,
        };

    private static string Clip(string value, int length) =>
        value == null ? null
        : value.Length <= length ? value
        : value[..length];
}
