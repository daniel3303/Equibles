using System.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Calendars;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Worker;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Yahoo.HostedService.Services;

internal readonly record struct PriceSeriesKey(Guid CommonStockId, string ListedTicker);

internal readonly record struct PriceSeriesTarget(
    string Ticker,
    Guid CommonStockId,
    bool IsPrimary,
    bool RequiresFullHistory = false,
    DateTime? YahooEnrichmentAttemptedAt = null
)
{
    public PriceSeriesKey Key => new(CommonStockId, Ticker);
}

internal readonly record struct LockedPriceSeries(CommonStock Stock, bool IsPrimary);

internal readonly record struct AppliedSplitBoundary(
    Guid SplitId,
    DateTime AppliedTime,
    decimal Numerator,
    decimal Denominator,
    decimal? CloseBefore,
    decimal? CloseAfter
);

internal readonly record struct SplitBasisDefinition(
    DateOnly EffectiveDate,
    decimal Numerator,
    decimal Denominator
);

[Service]
public class YahooPriceImportService
{
    private const double MinimumReferenceHistoryCoverageShare = 0.90;
    private const int InsertBatchSize = 500;
    private const int AppliedSplitBasisAuditLookbackDays = 180;
    private const decimal MaterialSplitRatioFloor = 0.5m;
    private const decimal MaterialSplitRatioCeiling = 2m;
    private const decimal SplitRatioMatchTolerance = 0.25m;
    private const decimal MaxPriceValue = 99_999_999_999_999.9999m; // numeric(18,4) ceiling

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<YahooPriceImportService> _logger;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly TickerMapService _tickerMapService;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;
    private readonly YahooPriceScraperOptions _scraperOptions;

    public bool HasEnrichmentBacklog { get; private set; }

    public YahooPriceImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<YahooPriceImportService> logger,
        IYahooFinanceClient yahooClient,
        TickerMapService tickerMapService,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions,
        IOptions<YahooPriceScraperOptions> scraperOptions
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _yahooClient = yahooClient;
        _tickerMapService = tickerMapService;
        _errorReporter = errorReporter;
        _workerOptions = workerOptions.Value;
        _scraperOptions = scraperOptions.Value;
    }

    public Task Import(CancellationToken cancellationToken) =>
        Import(includeEnrichment: true, cancellationToken);

    /// <summary>
    /// One price-sync cycle over the tracked universe. With <paramref name="includeEnrichment"/>
    /// false only the incremental chart fetch runs per stock (1 Yahoo call, and none at all for a
    /// stock that is already current — see the settled-trading-day gate in ImportTicker), so the
    /// worker can run frequent cheap price cycles. When true, a bounded batch of stocks whose
    /// persisted attempt time is due receives the key-statistics + company-profile calls (2 extra
    /// Yahoo calls per stock, the bulk of a cycle's traffic).
    /// </summary>
    public async Task Import(bool includeEnrichment, CancellationToken cancellationToken)
    {
        HasEnrichmentBacklog = false;
        var tickerMap = await _tickerMapService.Build(
            _workerOptions.TickersToSync,
            cancellationToken
        );
        var priceTargets = await BuildPriceSeriesTargets(
            tickerMap.Values.Distinct().ToList(),
            cancellationToken
        );
        _logger.LogInformation(
            "Starting Yahoo price sync for {SeriesCount} listed symbols across {StockCount} stocks (enrichment: {Enrichment})",
            priceTargets.Count,
            tickerMap.Count,
            includeEnrichment ? "on" : "off"
        );

        // Before the forward-only incremental append: reconcile listed series whose captured split
        // or cash dividend is still pending. GetSyncStartDate only appends and cannot revisit old
        // rows, so re-pull the full provider-served history and replace the series atomically.
        await ReconcilePendingCorporateActions(
            DateOnly.FromDateTime(DateTime.UtcNow),
            cancellationToken
        );

        // Crawl the recently-active stocks first, stalest-first within them, and the long-dormant
        // tail afterwards (see OrderByCrawlPriority for why the plain stalest-first order starved
        // the daily lane).
        var crawlOrder = await OrderByCrawlPriority(priceTargets, cancellationToken);

        // Prices for the WHOLE universe first, enrichment only afterwards. The two used to be
        // interleaved per ticker, which made every stock cost three Yahoo calls instead of one and
        // stretched a full pass to hours — and since an interrupted cycle simply restarts from the
        // top, a worker that restarts more often than a pass takes (a deploy, say) would keep
        // re-walking the head and never reach the stocks missing yesterday's bar. Prices are the
        // time-critical half and are nearly free once a stock is current (the settled-trading-day
        // gate makes an up-to-date stock cost zero calls), so they must never queue behind
        // enrichment traffic for the stock in front.
        var totalInserted = await ImportPrices(crawlOrder, cancellationToken);

        _logger.LogInformation(
            "Yahoo price sync complete. Inserted {Count} new price records",
            totalInserted
        );

        if (includeEnrichment)
        {
            var primaryOrder = crawlOrder.Where(target => target.IsPrimary).ToList();
            await ImportEnrichment(primaryOrder, cancellationToken);
        }
    }

    private async Task<List<PriceSeriesTarget>> BuildPriceSeriesTargets(
        IReadOnlyCollection<Guid> stockIds,
        CancellationToken cancellationToken
    )
    {
        if (stockIds.Count == 0)
            return [];

        using var scope = _scopeFactory.CreateScope();
        var stockRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var stocks = await stockRepository
            .GetByIds(stockIds)
            .Select(stock => new
            {
                stock.Id,
                stock.Ticker,
                stock.SecondaryTickers,
                stock.ReferenceTickers,
                stock.PriceHistoryBackfilledTickers,
                stock.YahooEnrichmentAttemptedAt,
            })
            .ToListAsync(cancellationToken);

        var targets = new List<PriceSeriesTarget>();
        foreach (var stock in stocks)
        {
            if (string.IsNullOrWhiteSpace(stock.Ticker))
                continue;

            var primaryTicker = TickerNormalizer.NormalizePrimary(stock.Ticker);
            if (primaryTicker == null)
                continue;

            var referenceTickers = (stock.ReferenceTickers ?? [])
                .Select(TickerNormalizer.NormalizeListed)
                .Where(ticker => ticker != null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var backfilledTickers = (stock.PriceHistoryBackfilledTickers ?? [])
                .Select(TickerNormalizer.NormalizeListed)
                .Where(ticker => ticker != null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            targets.Add(
                new PriceSeriesTarget(
                    primaryTicker,
                    stock.Id,
                    IsPrimary: true,
                    RequiresFullHistory: referenceTickers.Contains(primaryTicker)
                        && !backfilledTickers.Contains(primaryTicker),
                    YahooEnrichmentAttemptedAt: stock.YahooEnrichmentAttemptedAt
                )
            );

            foreach (
                var secondaryTicker in (stock.SecondaryTickers ?? [])
                    .Concat(stock.ReferenceTickers ?? [])
                    .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
                    .Select(TickerNormalizer.NormalizeListed)
                    .Where(ticker => ticker != null && ticker != primaryTicker)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                targets.Add(
                    new PriceSeriesTarget(
                        secondaryTicker,
                        stock.Id,
                        IsPrimary: false,
                        RequiresFullHistory: referenceTickers.Contains(secondaryTicker)
                            && !backfilledTickers.Contains(secondaryTicker)
                    )
                );
            }
        }

        return targets;
    }

    private static async Task<LockedPriceSeries?> LockPriceSeries(
        CommonStockRepository stockRepository,
        PriceSeriesTarget target,
        CancellationToken cancellationToken
    )
    {
        var stock = await stockRepository.GetForUpdate(target.CommonStockId, cancellationToken);
        var resolvedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, target.Ticker);
        if (
            resolvedTicker == null
            || !string.Equals(resolvedTicker, target.Ticker, StringComparison.OrdinalIgnoreCase)
        )
            return null;

        return new LockedPriceSeries(
            stock,
            string.Equals(resolvedTicker, stock.Ticker, StringComparison.OrdinalIgnoreCase)
        );
    }

    // Pass 1 — the settled daily bars. One Yahoo call per listed symbol that actually needs one.
    private async Task<int> ImportPrices(
        List<PriceSeriesTarget> crawlOrder,
        CancellationToken cancellationToken
    )
    {
        var totalInserted = 0;
        var fetched = 0;
        var fetchedWithNothingNew = 0;

        foreach (var target in crawlOrder)
        {
            var ticker = target.Ticker;
            cancellationToken.ThrowIfCancellationRequested();

            // Resolved per ticker, not once per cycle: a multi-hour crawl straddles the UTC
            // midnight rollover, and a cycle-start snapshot would keep excluding the just-settled
            // bar for every stock processed after midnight — a cycle starting 23:50 UTC used to
            // ship a whole day late for the entire universe.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            try
            {
                var result = await ImportTicker(target, today, cancellationToken);
                totalInserted += result.Inserted;
                if (result.Fetched)
                {
                    fetched++;
                    if (result.Inserted == 0)
                        fetchedWithNothingNew++;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to fetch prices for {Ticker}, skipping", ticker);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown, not a per-ticker fault — rethrow so the worker's cancellation
                // handling sees it instead of recording a phantom error row per deploy.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing prices for {Ticker}", ticker);
                await _errorReporter.Report(
                    ErrorSource.YahooPriceScraper,
                    $"ImportTicker({ticker})",
                    ex
                );
            }
        }

        WarnIfUpstreamServedNothing(crawlOrder.Count, fetched, fetchedWithNothingNew);
        return totalInserted;
    }

    // A barren fetch — one that called the provider and inserted nothing — is only evidence of an
    // upstream outage in AGGREGATE, and the aggregate that matters is the universe, not the cycle's
    // fetches. A quiet cycle legitimately looks close to 100% barren: once the active set is
    // current, the only stocks still fetched are the dormant tail and the thin lines that did not
    // trade the session (~600 of ~8,400 on a normal day), and every one of them returns nothing by
    // design. The outage signature differs in SIZE, not ratio — the whole universe fetching and
    // inserting nothing — so the warning requires the barren set to be a large share of the
    // universe, which a quiet cycle's tail (~7%) never reaches.
    //
    // This exists because that outage is otherwise INVISIBLE. On 2026-07-24 Yahoo served the entire
    // session's daily bars with null OHLC; the importer correctly refused to store them, so the lane
    // ran flat out — thousands of successful HTTP 200s — and wrote nothing, with every log line at
    // Information saying the cycle had started and completed normally. The per-fetch detail that
    // would have shown it is at Debug, which production does not emit.
    private void WarnIfUpstreamServedNothing(
        int universeSize,
        int fetched,
        int fetchedWithNothingNew
    )
    {
        if (universeSize <= 0 || fetched < MinFetchesForUpstreamWarning)
            return;

        var barrenRatio = (double)fetchedWithNothingNew / fetched;
        if (barrenRatio < BarrenFetchWarningRatio)
            return;

        if ((double)fetchedWithNothingNew / universeSize < BarrenUniverseShareForWarning)
            return;

        _logger.LogWarning(
            "Yahoo served no new settled bars for {Barren} of {Fetched} listed symbols that needed one "
                + "({Percent:P0} of the {Universe}-symbol universe). The price feed is likely "
                + "publishing incomplete bars upstream; stored prices will stay stale until it "
                + "recovers.",
            fetchedWithNothingNew,
            fetched,
            (double)fetchedWithNothingNew / universeSize,
            universeSize
        );
    }

    // Small crawls (a weekend no-op, a tiny configured universe) are not evidence of anything.
    private const int MinFetchesForUpstreamWarning = 200;

    // Deliberately high: a normal catch-up cycle inserts for nearly every stock it fetches, so this
    // only trips when the feed is broadly refusing to serve usable bars.
    private const double BarrenFetchWarningRatio = 0.9;

    // The size half of the signature. The real 2026-07-24 outage put ~73% of the universe in the
    // barren set; a healthy quiet cycle's dormant-plus-thin tail sits near 7%. Anything above a
    // quarter of the universe returning nothing is not a tail.
    private const double BarrenUniverseShareForWarning = 0.25;

    // Pass 2 — key statistics + company profile. Two extra Yahoo calls per stock and the bulk of a
    // cycle's traffic, which is why it runs in restart-safe batches AND strictly after prices.
    private async Task ImportEnrichment(
        List<PriceSeriesTarget> crawlOrder,
        CancellationToken cancellationToken
    )
    {
        var interval = TimeSpan.FromHours(Math.Max(0, _scraperOptions.EnrichmentIntervalHours));
        var selection = SelectEnrichmentBatch(
            crawlOrder,
            DateTime.UtcNow,
            interval,
            Math.Max(1, _scraperOptions.EnrichmentBatchSize)
        );
        HasEnrichmentBacklog = selection.Remaining > 0;
        if (selection.Targets.Count == 0)
            return;

        _logger.LogInformation(
            "Enriching {Count} due stocks; {Remaining} will continue after the next price pass",
            selection.Targets.Count,
            selection.Remaining
        );

        foreach (var target in selection.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnrichTarget(target, cancellationToken);
        }
    }

    internal static (List<PriceSeriesTarget> Targets, int Remaining) SelectEnrichmentBatch(
        IReadOnlyCollection<PriceSeriesTarget> targets,
        DateTime now,
        TimeSpan interval,
        int batchSize
    )
    {
        var cutoff = now - interval;
        var due = targets
            .Where(target =>
                target.IsPrimary
                && (
                    target.YahooEnrichmentAttemptedAt == null
                    || target.YahooEnrichmentAttemptedAt <= cutoff
                )
            )
            .OrderBy(target => target.YahooEnrichmentAttemptedAt ?? DateTime.MinValue)
            .ThenBy(target => target.Ticker, StringComparer.Ordinal)
            .ThenBy(target => target.CommonStockId)
            .ToList();
        var batch = due.Take(Math.Max(1, batchSize)).ToList();
        return (batch, due.Count - batch.Count);
    }

    private async Task EnrichTarget(PriceSeriesTarget target, CancellationToken cancellationToken)
    {
        var ticker = target.Ticker;

        try
        {
            await SyncKeyStatistics(target, cancellationToken);
            await SyncCompanyProfile(target, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch enrichment for {Ticker}, skipping", ticker);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching {Ticker}", ticker);
            await _errorReporter.Report(ErrorSource.YahooPriceScraper, $"Enrich({ticker})", ex);
        }

        await StampEnrichmentAttempt(target, DateTime.UtcNow, cancellationToken);
    }

    private async Task StampEnrichmentAttempt(
        PriceSeriesTarget target,
        DateTime attemptedAt,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        await using var transaction = await stockRepo.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var lockedSeries = await LockPriceSeries(stockRepo, target, cancellationToken);
        if (lockedSeries is not { IsPrimary: true })
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        lockedSeries.Value.Stock.YahooEnrichmentAttemptedAt = attemptedAt;
        if (
            !await SaveStockChanges(
                stockRepo,
                target.CommonStockId,
                target.Ticker,
                cancellationToken
            )
        )
            return;
        await transaction.CommitAsync(cancellationToken);
    }

    // A stock whose newest stored bar is within this many calendar days is treated as actively
    // trading and belongs to the daily working set. Comfortably clears a long weekend plus a
    // holiday, so a healthy stock can never fall out of the set just because the market was shut.
    private const int ActivelyTradedWindowDays = 10;

    // Orders the crawl: actively-traded stocks first (stalest of them leading), long-dormant and
    // never-synced stocks after them, stalest-first within each group. One grouped MAX(Date) query
    // over the price table per cycle.
    //
    // Plain stalest-first is the obvious order and it was actively harmful. Sorting the whole
    // universe by last stored date puts the stocks that will never return data — delisted tickers,
    // bankruptcy-suffixed symbols, expired warrants, foreign OTC lines Yahoo does not serve — at
    // the front of EVERY cycle, because "no data for months" sorts as "stalest". They cost a call
    // each, yield nothing, and are re-paid on the next cycle: in production 617 such stocks sat
    // ahead of the 5,484 that were merely missing the previous session's bar, so the lane spent its
    // first ~23 minutes on hopeless work while the whole site showed a stale close.
    //
    // Splitting on recency fixes that without reintroducing starvation, which is what stalest-first
    // was guarding against. The dormant tail still runs every cycle, just second — and once the
    // working set is current it costs nearly nothing to walk (the settled-trading-day gate skips an
    // up-to-date stock without any Yahoo call), so the tail gets almost the entire cycle anyway.
    private async Task<List<PriceSeriesTarget>> OrderByCrawlPriority(
        List<PriceSeriesTarget> targets,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var priceRepo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        var rows = await priceRepo
            .GetAllSeries()
            .GroupBy(p => new { p.CommonStockId, p.ListedTicker })
            .Select(group => new
            {
                group.Key.CommonStockId,
                group.Key.ListedTicker,
                LastDate = group.Max(p => p.Date),
            })
            .ToListAsync(cancellationToken);
        var lastDates = rows.ToDictionary(
            row => new PriceSeriesKey(row.CommonStockId, row.ListedTicker),
            row => row.LastDate
        );

        return BuildCrawlOrder(targets, lastDates, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    // Pure so the priority rule is pinnable in tests without a database.
    internal static List<PriceSeriesTarget> BuildCrawlOrder(
        IReadOnlyCollection<PriceSeriesTarget> targets,
        IReadOnlyDictionary<PriceSeriesKey, DateOnly> lastDates,
        DateOnly today
    )
    {
        var activeSince = today.AddDays(-ActivelyTradedWindowDays);

        return targets
            .Select(target => new
            {
                Target = target,
                LastDate = lastDates.TryGetValue(target.Key, out var lastDate)
                    ? lastDate
                    : DateOnly.MinValue,
            })
            // false (0) sorts before true (1), so the actively-traded group leads.
            .OrderBy(x => x.LastDate < activeSince)
            .ThenBy(x => x.LastDate)
            .ThenBy(x => x.Target.Ticker, StringComparer.Ordinal)
            .ThenBy(x => x.Target.CommonStockId)
            .Select(x => x.Target)
            .ToList();
    }

    // Re-syncs the full price history of every exact listed series with an effective, unreconciled
    // split or dividend, capped per cycle. Future actions remain pending until the first settled
    // day after their action date. Copy the provider response instead of deriving adjustment ratios.
    private async Task ReconcilePendingCorporateActions(
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        await RequeueStampedSplitBasisMismatches(today, cancellationToken);

        PendingPriceReconciliationSelection selection;
        using (var scope = _scopeFactory.CreateScope())
        {
            var manager =
                scope.ServiceProvider.GetRequiredService<CorporateActionPriceReconciliationManager>();
            selection = await manager.SelectPendingSeries(
                _workerOptions.MaxCorporateActionPriceReconciliationsPerCycle,
                today,
                cancellationToken
            );
        }

        if (selection.Series.Count == 0)
            return;

        _logger.LogInformation(
            "Re-syncing full price history for {Count} listed series with pending corporate actions",
            selection.Series.Count
        );
        if (selection.Skipped > 0)
            _logger.LogInformation(
                "{Remaining} more listed series with pending corporate actions exceed the per-cycle cap "
                    + "and will be reconciled on a later cycle",
                selection.Skipped
            );

        var floor = PriceHistoryFloor();

        foreach (var series in selection.Series)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ReconcileStock(series, floor, today, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to fetch full history for {Ticker}; leaving its corporate actions pending",
                    series.ListedTicker
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown, not a per-stock fault — rethrow so the worker's cancellation
                // handling sees it instead of recording a phantom error row per deploy.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error reconciling full price history for {Ticker}",
                    series.ListedTicker
                );
                await _errorReporter.Report(
                    ErrorSource.YahooPriceScraper,
                    $"ReconcilePendingCorporateActions({series.ListedTicker})",
                    ex
                );
            }
        }
    }

    private async Task ReconcileStock(
        PendingPriceReconciliationSeries selectedSeries,
        DateOnly floor,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var target = new PriceSeriesTarget(
            selectedSeries.ListedTicker,
            selectedSeries.CommonStockId,
            IsPrimary: false
        );
        var chartData = await _yahooClient.GetChart(target.Ticker, floor, today);
        await CaptureSplits(target, chartData.Splits, cancellationToken);

        // A delisted/unresolved ticker returns no prices. Do NOT wipe the existing series in that
        // case — leave the split pending so a later run or another source can handle it.
        if (chartData.Prices.Count == 0)
        {
            _logger.LogWarning(
                "Yahoo returned no prices for {Ticker}; keeping existing rows and leaving its corporate actions pending",
                target.Ticker
            );
            return;
        }

        var splitBoundaries = selectedSeries
            .Splits.Select(split => new SplitBasisDefinition(
                split.EffectiveDate,
                split.Numerator,
                split.Denominator
            ))
            .Concat(
                chartData.Splits.Select(split => new SplitBasisDefinition(
                    split.Date,
                    split.Numerator,
                    split.Denominator
                ))
            )
            .Distinct()
            .ToList();
        if (
            !TryPutHistoryOnSingleSplitBasis(
                target.Ticker,
                chartData.Prices,
                splitBoundaries,
                today
            )
        )
            return;

        var replaced = await ReplaceStoredPrices(
            target,
            floor,
            today,
            chartData.Prices,
            cancellationToken
        );
        if (!replaced)
            return;

        // Dividends that still exactly match this response can be marked from the same fetch; a
        // concurrent restatement remains pending because the manager revalidates the locked row.
        var capturedDividends = await CaptureDividends(
            target,
            chartData.Dividends,
            cancellationToken
        );

        // A serve whose last bar predates a split's effective date passed the boundary check
        // vacuously (no post-effective close to compare), so it cannot certify that split's basis:
        // stamping it applied here parked BYND's 1-for-30 outside the pending queue while the
        // stored series stayed pre-split. Keep such splits pending; the fair queue retries them.
        var certifiableSeries = selectedSeries with
        {
            Splits = CertifiableSplits(selectedSeries.Splits, chartData.Prices.Max(p => p.Date)),
        };
        if (certifiableSeries.Splits.Count < selectedSeries.Splits.Count)
        {
            _logger.LogWarning(
                "Provider history for {Ticker} ends before {Count} pending split(s) became effective; keeping them pending",
                target.Ticker,
                selectedSeries.Splits.Count - certifiableSeries.Splits.Count
            );
        }

        // A split changes the share base, so refresh the authoritative current share count +
        // market cap by refetch, not arithmetic (#2879). A dividend-only price reconciliation is
        // complete after the atomic history replacement and must not depend on unrelated
        // quote-summary or EDGAR enrichment succeeding. Gate on the certifiable set: a provider
        // that has not served the split's first post-effective bar is serving pre-split
        // key statistics too.
        if (certifiableSeries.Splits.Count > 0)
            await SyncKeyStatistics(target, cancellationToken);

        using var scope = _scopeFactory.CreateScope();
        var manager =
            scope.ServiceProvider.GetRequiredService<CorporateActionPriceReconciliationManager>();
        var stamped = await manager.StampApplied(
            certifiableSeries,
            capturedDividends,
            today,
            DateTime.UtcNow,
            cancellationToken
        );
        _logger.LogInformation(
            "Reconciled {Ticker}: replaced stored price history and stamped {Count} corporate action(s) applied",
            target.Ticker,
            stamped
        );
    }

    private async Task RequeueStampedSplitBasisMismatches(
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var splitRepository = scope.ServiceProvider.GetRequiredService<StockSplitRepository>();
        var priceRepository = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        var appliedSince = DateTime.UtcNow.AddDays(-AppliedSplitBasisAuditLookbackDays);

        var boundaries = await splitRepository
            .GetAll()
            .Where(split =>
                split.PriceAdjustmentAppliedTime >= appliedSince
                && split.PriceSeriesTicker != null
                && split.EffectiveDate < today
                && split.Numerator > 0m
                && split.Denominator > 0m
                && (
                    split.Numerator / split.Denominator <= MaterialSplitRatioFloor
                    || split.Numerator / split.Denominator >= MaterialSplitRatioCeiling
                )
            )
            .Select(split => new AppliedSplitBoundary(
                split.Id,
                split.PriceAdjustmentAppliedTime!.Value,
                split.Numerator,
                split.Denominator,
                priceRepository
                    .GetAllSeries()
                    .Where(price =>
                        price.CommonStockId == split.CommonStockId
                        && price.ListedTicker == split.PriceSeriesTicker
                        && price.Date < split.EffectiveDate
                    )
                    .OrderByDescending(price => price.Date)
                    .Select(price => (decimal?)price.Close)
                    .FirstOrDefault(),
                priceRepository
                    .GetAllSeries()
                    .Where(price =>
                        price.CommonStockId == split.CommonStockId
                        && price.ListedTicker == split.PriceSeriesTicker
                        && price.Date >= split.EffectiveDate
                    )
                    .OrderBy(price => price.Date)
                    .Select(price => (decimal?)price.Close)
                    .FirstOrDefault()
            ))
            .ToListAsync(cancellationToken);

        var invalidMarkers = boundaries
            .Where(boundary =>
                IsSplitBoundaryDiscontinuous(
                    boundary.CloseBefore,
                    boundary.CloseAfter,
                    boundary.Numerator,
                    boundary.Denominator
                )
            )
            .Select(boundary => new AppliedSplitMarkerSnapshot(
                boundary.SplitId,
                boundary.AppliedTime
            ))
            .ToList();
        if (invalidMarkers.Count == 0)
            return;

        var manager =
            scope.ServiceProvider.GetRequiredService<CorporateActionPriceReconciliationManager>();
        var requeued = await manager.RequeueAppliedSplits(invalidMarkers, cancellationToken);
        _logger.LogWarning(
            "Requeued {Count} split reconciliation marker(s) whose stored history still crossed price bases",
            requeued
        );
    }

    // Accepts a fetched history only when it sits on one split basis, restating it first when the
    // authoritative captured ratio explains the jump. Yahoo can serve an unrestated history for
    // days after a split becomes effective; waiting for it froze the whole series (no new bars,
    // no rebase) for exactly the names customers are watching. The restatement is deterministic
    // arithmetic off the captured ratio — never an inference — because it only fires when the
    // observed boundary jump already matches that ratio.
    private bool TryPutHistoryOnSingleSplitBasis(
        string ticker,
        List<HistoricalPrice> prices,
        IReadOnlyCollection<SplitBasisDefinition> splits,
        DateOnly today
    )
    {
        var restated = RestateHistoryAcrossKnownSplits(prices, splits, today);
        if (restated > 0)
        {
            _logger.LogWarning(
                "Restated {Ticker}'s fetched history across {Count} captured split boundary(ies) the provider had not adjusted yet",
                ticker,
                restated
            );
        }

        return !ShouldRejectSplitBearingHistory(ticker, prices, splits);
    }

    // Puts the segment before each straddled effective split onto the post-split basis: prices
    // scale by denominator/numerator and volumes by numerator/denominator, exactly cancelling the
    // boundary jump. A boundary is restated ONLY when its observed jump matches the captured
    // ratio — restating on the ratio alone would double-apply a split the provider had already
    // adjusted, and a future (announced) split must never restate anything.
    internal static int RestateHistoryAcrossKnownSplits(
        List<HistoricalPrice> prices,
        IEnumerable<SplitBasisDefinition> splits,
        DateOnly today
    )
    {
        var restated = 0;
        foreach (var split in splits.OrderByDescending(split => split.EffectiveDate))
        {
            if (split.EffectiveDate > today || split.Numerator <= 0m || split.Denominator <= 0m)
                continue;
            if (
                !HasSplitBasisDiscontinuity(
                    prices,
                    split.EffectiveDate,
                    split.Numerator,
                    split.Denominator
                )
            )
                continue;

            var priceFactor = split.Denominator / split.Numerator;
            var volumeFactor = split.Numerator / split.Denominator;
            foreach (var price in prices)
            {
                if (price.Date >= split.EffectiveDate)
                    continue;

                price.Open = Math.Round(price.Open * priceFactor, 4);
                price.High = Math.Round(price.High * priceFactor, 4);
                price.Low = Math.Round(price.Low * priceFactor, 4);
                price.Close = Math.Round(price.Close * priceFactor, 4);
                price.AdjustedClose = Math.Round(price.AdjustedClose * priceFactor, 4);
                price.Volume = (long)Math.Round(price.Volume * volumeFactor);
            }

            restated++;
        }

        return restated;
    }

    // The splits a replacement history can actually certify: only a serve containing at least one
    // bar on or after a split's effective date can prove the series is on that split's basis. A
    // serve ending earlier passes the discontinuity check vacuously and must leave the split
    // pending instead of stamping it applied.
    internal static IReadOnlyList<PendingSplitSnapshot> CertifiableSplits(
        IReadOnlyList<PendingSplitSnapshot> splits,
        DateOnly lastServedDate
    )
    {
        return splits.Where(split => split.EffectiveDate <= lastServedDate).ToList();
    }

    private bool ShouldRejectSplitBearingHistory(
        string ticker,
        IReadOnlyCollection<HistoricalPrice> prices,
        IEnumerable<SplitBasisDefinition> splits
    )
    {
        foreach (var split in splits)
        {
            if (
                !HasSplitBasisDiscontinuity(
                    prices,
                    split.EffectiveDate,
                    split.Numerator,
                    split.Denominator
                )
            )
                continue;

            _logger.LogWarning(
                "Yahoo full history for {Ticker} still straddles split {EffectiveDate} ({Numerator}:{Denominator}); keeping the existing series and the action pending",
                ticker,
                split.EffectiveDate,
                split.Numerator,
                split.Denominator
            );
            return true;
        }

        return false;
    }

    internal static bool HasSplitBasisDiscontinuity(
        IReadOnlyCollection<HistoricalPrice> prices,
        DateOnly effectiveDate,
        decimal numerator,
        decimal denominator
    )
    {
        var closeBefore = prices
            .Where(price => price.Date < effectiveDate)
            .OrderByDescending(price => price.Date)
            .Select(price => (decimal?)price.Close)
            .FirstOrDefault();
        var closeAfter = prices
            .Where(price => price.Date >= effectiveDate)
            .OrderBy(price => price.Date)
            .Select(price => (decimal?)price.Close)
            .FirstOrDefault();

        return IsSplitBoundaryDiscontinuous(closeBefore, closeAfter, numerator, denominator);
    }

    internal static bool IsSplitBoundaryDiscontinuous(
        decimal? closeBefore,
        decimal? closeAfter,
        decimal numerator,
        decimal denominator
    )
    {
        if (
            closeBefore is not > 0m
            || closeAfter is not > 0m
            || numerator <= 0m
            || denominator <= 0m
        )
            return false;

        var splitRatio = numerator / denominator;
        if (splitRatio > MaterialSplitRatioFloor && splitRatio < MaterialSplitRatioCeiling)
            return false;

        var observedRatio = closeBefore.Value / closeAfter.Value;
        return Math.Abs(observedRatio - splitRatio) / splitRatio <= SplitRatioMatchTolerance;
    }

    // Transactionally swaps a stock's stored rows in [floor, today] for the fresh provider-served
    // series. Returns false without touching the stored rows when there is nothing usable to store
    // (all rows overflowed the numeric ceiling, or the parent CommonStock was removed) so a stock
    // is never left with an empty series.
    private async Task<bool> ReplaceStoredPrices(
        PriceSeriesTarget target,
        DateOnly floor,
        DateOnly today,
        List<HistoricalPrice> prices,
        CancellationToken cancellationToken
    )
    {
        var freshRows = MapFreshRows(target.CommonStockId, prices, target.Ticker, today);
        if (freshRows.Count == 0)
        {
            _logger.LogWarning(
                "No storable prices for {Ticker} after the numeric range guard; keeping existing rows",
                target.Ticker
            );
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        var replaced = await ReplacePriceRows(
            repo,
            stockRepo,
            target,
            floor,
            today,
            freshRows,
            cancellationToken
        );
        if (!replaced)
            _logger.LogWarning(
                "Skipping reconcile for {Ticker}: it no longer belongs to CommonStock {Id}",
                target.Ticker,
                target.CommonStockId
            );
        return replaced;
    }

    // The transactional core of the replacement: delete the stock's rows in [floor, today], then
    // bulk-insert the fresh rows in batches, all in one transaction so the stock is never left with
    // a partial series on failure. Takes the repo so it is unit-testable without a live worker.
    private static async Task<bool> ReplacePriceRows(
        DailyStockPriceRepository repo,
        CommonStockRepository stockRepo,
        PriceSeriesTarget target,
        DateOnly floor,
        DateOnly today,
        List<DailyStockPrice> freshRows,
        CancellationToken cancellationToken
    )
    {
        // Never delete the stored series when there is nothing to replace it with. The caller
        // already guards empty fetches upstream; keeping the invariant local too means the
        // transaction (and its delete) is never opened for an empty replacement.
        if (freshRows.Count == 0)
            return false;

        await using var transaction = await repo.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        try
        {
            var lockedSeries = await LockPriceSeries(stockRepo, target, cancellationToken);
            if (lockedSeries == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            var existing = await repo.GetAllSeries()
                .Where(p =>
                    p.CommonStockId == target.CommonStockId
                    && p.ListedTicker == target.Ticker
                    && p.Date >= floor
                    && p.Date <= today
                )
                .ToListAsync(cancellationToken);
            if (existing.Count > 0)
            {
                repo.Delete(existing);
                await repo.SaveChanges();
            }

            foreach (var batch in freshRows.Chunk(InsertBatchSize))
            {
                repo.AddRange(batch);
                await repo.SaveChanges();
            }

            if (target.RequiresFullHistory)
            {
                lockedSeries.Value.Stock.PriceHistoryBackfilledTickers = lockedSeries
                    .Value.Stock.PriceHistoryBackfilledTickers.Append(target.Ticker)
                    .Select(TickerNormalizer.NormalizeListed)
                    .Where(ticker => ticker != null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(ticker => ticker, StringComparer.Ordinal)
                    .ToList();
                await stockRepo.SaveChanges();
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private List<DailyStockPrice> MapFreshRows(
        Guid commonStockId,
        List<HistoricalPrice> prices,
        string ticker,
        DateOnly today
    )
    {
        var overflowDates = WarnAndCollectOverflowDates(prices, ticker);
        var invalidOhlcDates = WarnAndCollectInvalidOhlcDates(prices, ticker);
        return prices
            .Where(p => !overflowDates.Contains(p.Date))
            .Where(p => !invalidOhlcDates.Contains(p.Date))
            .Where(p => IsSettledDailyBar(p.Date, today))
            .Select(p => new DailyStockPrice
            {
                CommonStockId = commonStockId,
                ListedTicker = ticker,
                Date = p.Date,
                Open = p.Open,
                High = p.High,
                Low = p.Low,
                Close = p.Close,
                AdjustedClose = p.AdjustedClose,
                Volume = p.Volume,
            })
            .ToList();
    }

    // Yahoo's daily chart includes the current, still-open trading day as a live candle: a partial
    // OHLC quartet and partial volume that keep changing until the session closes. Persisting it is
    // wrong twice over — the "Close" is really an intraday snapshot, and the importer is insert-only
    // (a date already present is never updated, see PersistPrices), so that partial bar freezes and
    // the real close never overwrites it. Only store bars strictly before the current UTC date; the
    // day's settled bar is appended by the first pass over the stock after the date has rolled over
    // (always after a US market close), so the daily series holds settled closes only.
    private static bool IsSettledDailyBar(DateOnly barDate, DateOnly today) => barDate < today;

    // A chart fetch can only yield new rows when at least one NYSE trading day lies in
    // [startDate, today) — the dates that are both unsynced and already settled. Gating the fetch
    // on that keeps frequent price cycles cheap: a stock that is already current costs zero Yahoo
    // calls until the next settled session exists, and weekend/holiday cycles are no-ops for the
    // whole universe instead of ~8k fruitless chart calls each. An empty window (startDate >=
    // today) is covered by the same rule. Full-day non-NYSE closures aren't modeled, so at worst a
    // stock is fetched and yields nothing — never skipped when a settled bar could exist.
    private static bool HasSettledTradingDay(DateOnly startDate, DateOnly today)
    {
        for (var date = startDate; date < today; date = date.AddDays(1))
        {
            if (UsMarketCalendar.IsTradingDay(date))
                return true;
        }
        return false;
    }

    // How far back a hole in the stored series is still worth re-requesting. The sync start date is
    // forward-only (last stored + 1), which is what keeps cycles cheap but also means any session
    // the upstream feed failed to serve is lost the moment a LATER bar lands and moves the start
    // date past it. On 2026-07-24 Yahoo served that whole session's daily bars with null OHLC, so
    // ~5,484 stocks were about to keep a permanent one-session hole once Monday's bar settled.
    //
    // Re-asking for a missing recent session costs nothing extra: it widens the SAME single chart
    // request the stock was already going to make, and PersistPrices is insert-only so re-served
    // bars that are already stored are discarded. The window is deliberately short — a session the
    // feed genuinely has no bar for (a halt, or a stock that simply did not trade) would otherwise
    // be re-requested forever, exactly the "hopeless work every cycle" pathology that the crawl
    // ordering had to be fixed for. After GapHealWindowDays the hole ages out and is left alone.
    private const int GapHealWindowDays = 10;

    // Outcome of one stock's price import. The Fetched flag is what distinguishes "already current,
    // no call made" from "called Yahoo and it gave us nothing usable" — indistinguishable from an
    // insert count alone, and the difference between a healthy cycle and a silent upstream outage.
    private readonly record struct TickerImportResult(bool Fetched, int Inserted);

    private static readonly TickerImportResult NoFetchNeeded = new(Fetched: false, Inserted: 0);

    private async Task<TickerImportResult> ImportTicker(
        PriceSeriesTarget target,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var startDate = await ResolveStartDate(target, today, cancellationToken);
        if (!HasSettledTradingDay(startDate, today))
            return NoFetchNeeded;

        // One chart fetch yields the price bars plus any split and dividend
        // events for the window — capture both off the same response, no extra
        // HTTP.
        var chartData = await _yahooClient.GetChart(target.Ticker, startDate, today);

        if (target.RequiresFullHistory)
        {
            await CaptureSplits(target, chartData.Splits, cancellationToken);
            if (!IsCompleteReferenceHistory(chartData, PriceHistoryFloor(), today))
            {
                _logger.LogWarning(
                    "Yahoo returned incomplete full history for reference listing {Ticker}; keeping its grouped bootstrap rows pending",
                    target.Ticker
                );
                return new TickerImportResult(Fetched: true, Inserted: 0);
            }

            if (
                !TryPutHistoryOnSingleSplitBasis(
                    target.Ticker,
                    chartData.Prices,
                    chartData
                        .Splits.Select(split => new SplitBasisDefinition(
                            split.Date,
                            split.Numerator,
                            split.Denominator
                        ))
                        .ToList(),
                    today
                )
            )
                return new TickerImportResult(Fetched: true, Inserted: 0);

            var replaced = await ReplaceStoredPrices(
                target,
                PriceHistoryFloor(),
                today,
                chartData.Prices,
                cancellationToken
            );
            if (replaced)
            {
                _logger.LogInformation(
                    "Replaced grouped bootstrap rows with full Yahoo history for reference listing {Ticker}",
                    target.Ticker
                );
                await CaptureDividends(target, chartData.Dividends, cancellationToken);
            }
            return new TickerImportResult(
                Fetched: true,
                Inserted: replaced ? chartData.Prices.Count : 0
            );
        }

        // Every exact listing needs a full-history rebase when its chart reports a split: Yahoo
        // retroactively adjusts old bars while the ordinary importer only appends. Doing this for
        // both the snapshotted primary and secondaries makes a concurrent designation change
        // harmless. Issuer-level action capture below independently locks and requires whichever
        // listing is primary at write time.
        var floor = PriceHistoryFloor();
        if (chartData.Splits.Count > 0 && startDate == floor)
        {
            await CaptureSplits(target, chartData.Splits, cancellationToken);
            if (
                !TryPutHistoryOnSingleSplitBasis(
                    target.Ticker,
                    chartData.Prices,
                    chartData
                        .Splits.Select(split => new SplitBasisDefinition(
                            split.Date,
                            split.Numerator,
                            split.Denominator
                        ))
                        .ToList(),
                    today
                )
            )
                return new TickerImportResult(Fetched: true, Inserted: 0);
        }

        if (chartData.Splits.Count > 0 && startDate > floor)
        {
            await CaptureSplits(target, chartData.Splits, cancellationToken);
            var fullChart = await _yahooClient.GetChart(target.Ticker, floor, today);
            await CaptureSplits(target, fullChart.Splits, cancellationToken);
            if (fullChart.Prices.Count == 0)
            {
                _logger.LogWarning(
                    "Yahoo returned no full history for split on {Ticker}; keeping its stored series",
                    target.Ticker
                );
                return new TickerImportResult(Fetched: true, Inserted: 0);
            }

            var splitBoundaries = chartData
                .Splits.Concat(fullChart.Splits)
                .Select(split => new SplitBasisDefinition(
                    split.Date,
                    split.Numerator,
                    split.Denominator
                ))
                .Distinct()
                .ToList();
            if (
                !TryPutHistoryOnSingleSplitBasis(
                    target.Ticker,
                    fullChart.Prices,
                    splitBoundaries,
                    today
                )
            )
                return new TickerImportResult(Fetched: true, Inserted: 0);

            var replaced = await ReplaceStoredPrices(
                target,
                floor,
                today,
                fullChart.Prices,
                cancellationToken
            );
            if (replaced)
            {
                _logger.LogInformation(
                    "Reconciled {Ticker}: replaced its full listed price history",
                    target.Ticker
                );
                await CaptureDividends(target, chartData.Dividends, cancellationToken);
            }
            return new TickerImportResult(Fetched: true, Inserted: 0);
        }

        var inserted = await PersistPrices(target, chartData.Prices, today, cancellationToken);

        // The capture paths lock and revalidate the current primary. A stale crawl target can
        // therefore keep its exact prices but cannot write issuer-level actions.
        await CaptureSplits(target, chartData.Splits, cancellationToken);
        await CaptureDividends(target, chartData.Dividends, cancellationToken);

        return new TickerImportResult(Fetched: true, Inserted: inserted);
    }

    private DateOnly PriceHistoryFloor() =>
        _workerOptions.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : new DateOnly(2020, 1, 1);

    private static bool IsCompleteReferenceHistory(
        YahooChartData chartData,
        DateOnly floor,
        DateOnly today
    )
    {
        var storableDates = chartData
            .Prices.Where(price => !HasOverflowPrice(price))
            .Where(price => !IsInvalidOhlc(price))
            .Where(price => IsSettledDailyBar(price.Date, today))
            .Select(price => price.Date)
            .ToList();
        if (storableDates.Count == 0)
            return false;

        var firstTradeDate = chartData.FirstTradeDate ?? floor;
        var expectedFirst = firstTradeDate > floor ? firstTradeDate : floor;
        while (!UsMarketCalendar.IsTradingDay(expectedFirst))
            expectedFirst = expectedFirst.AddDays(1);
        if (expectedFirst >= today)
            return false;

        var expectedLast = UsMarketCalendar.PreviousTradingDay(today);
        if (storableDates.Min() > expectedFirst || storableDates.Max() < expectedLast)
            return false;

        var expectedSessions = 0;
        for (var date = expectedFirst; date <= expectedLast; date = date.AddDays(1))
        {
            if (UsMarketCalendar.IsTradingDay(date))
                expectedSessions++;
        }
        var coveredSessions = storableDates
            .Where(date => date >= expectedFirst && date <= expectedLast)
            .Distinct()
            .Count();
        return coveredSessions
            >= (int)Math.Ceiling(expectedSessions * MinimumReferenceHistoryCoverageShare);
    }

    private async Task<int> PersistPrices(
        PriceSeriesTarget target,
        List<HistoricalPrice> prices,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        if (prices.Count == 0)
            return 0;

        var freshRows = MapFreshRows(target.CommonStockId, prices, target.Ticker, today);
        if (freshRows.Count == 0)
            return 0;

        // The new exact-listing table starts empty after the additive migration. Publish a
        // listing's first full response in one transaction so readers see either no exact series
        // or the complete backfill, never the first 500-row batch of a multi-batch insert.
        if (!await HasStoredSeries(target, cancellationToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
            var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
            var stored = await ReplacePriceRows(
                repo,
                stockRepo,
                target,
                PriceHistoryFloor(),
                today,
                freshRows,
                cancellationToken
            );
            return stored ? freshRows.Count : 0;
        }

        // Load existing dates covering the actual response range to avoid duplicates.
        var minDate = prices.Min(p => p.Date);
        var maxDate = prices.Max(p => p.Date);
        var existingDates = await GetExistingDates(target, minDate, maxDate, cancellationToken);

        // Runs before the insert path's early return: a stock whose only revised bar is one it
        // ALREADY stored has nothing new to insert, and that is exactly the stock whose settled
        // OHLC/volume still needs correcting.
        await ResettleStoredBars(target, freshRows, today, cancellationToken);

        var newPrices = freshRows.Where(p => !existingDates.Contains(p.Date)).ToList();

        if (newPrices.Count == 0)
            return 0;

        var inserted = await BatchPersister.Persist(newPrices, InsertBatchSize, FlushPriceBatch);

        _logger.LogDebug("Inserted {Count} prices for {Ticker}", inserted, target.Ticker);
        return inserted;
    }

    private async Task<bool> HasStoredSeries(
        PriceSeriesTarget target,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        return await repo.GetAllSeries()
            .AnyAsync(
                p => p.CommonStockId == target.CommonStockId && p.ListedTicker == target.Ticker,
                cancellationToken
            );
    }

    // Corrects stored bars that were captured before the feed settled them.
    //
    // A bar becomes storable the moment its date rolls over in UTC, which is only four hours after
    // the 20:00 UTC close. The feed serves a daily bar that early with an unsettled volume — the
    // closing cross and late-reported off-exchange prints are still landing — and can revise both
    // OHLC and volume overnight. PersistPrices is insert-only, so that first partial figure used to
    // be permanent.
    //
    // Re-reading the window off the SAME chart response the stock was already fetching costs no
    // extra upstream call — ResolveStartDate only widens a request that was going to happen anyway.
    // Steady state therefore corrects each session when the next session syncs.
    private async Task<int> ResettleStoredBars(
        PriceSeriesTarget target,
        List<DailyStockPrice> freshRows,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var windowStart = ResettleWindowStart(today, _scraperOptions.VolumeResettleWindowDays);

        // Last-wins rather than ToDictionary: a feed that repeats a date must not throw here, and
        // the insert path already assumes the response holds one bar per date.
        var fetched = new Dictionary<DateOnly, DailyStockPrice>();
        foreach (var row in freshRows)
        {
            if (row.Date >= windowStart)
                fetched[row.Date] = row;
        }

        if (fetched.Count == 0)
            return 0;

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        await using var transaction = await repo.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        if (await LockPriceSeries(stockRepo, target, cancellationToken) == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Skipping settled-bar repair for {Ticker}: it no longer belongs to CommonStock {Id}",
                target.Ticker,
                target.CommonStockId
            );
            return 0;
        }
        var stored = await repo.GetAllSeries()
            .Where(p =>
                p.CommonStockId == target.CommonStockId
                && p.ListedTicker == target.Ticker
                && p.Date >= windowStart
                && p.Date < today
            )
            .ToListAsync(cancellationToken);

        var corrected = 0;
        var skippedOnBasis = 0;
        foreach (var row in stored)
        {
            if (!fetched.TryGetValue(row.Date, out var bar))
                continue;

            // Both records must describe the session on the SAME split basis before any price or
            // volume field can be reconciled — see IsSameSplitBasis.
            if (!IsSameSplitBasis(row.Close, bar.Close))
            {
                skippedOnBasis++;
                continue;
            }

            var changed = false;
            if (
                row.Open != bar.Open
                || row.High != bar.High
                || row.Low != bar.Low
                || row.Close != bar.Close
            )
            {
                row.Open = bar.Open;
                row.High = bar.High;
                row.Low = bar.Low;
                row.Close = bar.Close;
                changed = true;
            }

            if (IsVolumeUpgrade(row.Volume, bar.Volume))
            {
                row.Volume = bar.Volume;
                changed = true;
            }

            if (changed)
                corrected++;
        }

        // A basis mismatch is the only place the store's divergence from the feed's served basis
        // is ever visible — the reconcile has already stamped the split as applied and will not
        // revisit the stock — so surface it rather than dropping the signal silently.
        if (skippedOnBasis > 0)
        {
            _logger.LogInformation(
                "Skipped {Count} stored bars for {Ticker}: stored close disagrees with the feed's split basis",
                skippedOnBasis,
                target.Ticker
            );
        }

        if (corrected == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        // The rows are tracked, so saving the mutations is enough — no repo.Update, which
        // would mark every column dirty and clobber a concurrent split reconcile's price basis.
        await repo.SaveChanges();
        await transaction.CommitAsync(cancellationToken);
        _logger.LogDebug("Corrected {Count} stored bars for {Ticker}", corrected, target.Ticker);
        return corrected;
    }

    /// <summary>
    /// Repairs a bounded batch of impossible historical OHLC rows from a fresh authoritative
    /// response. A row with no valid same-basis replacement is removed rather than continuing to
    /// publish data that is known to be impossible. Returns true once the corpus is clean.
    /// </summary>
    public async Task<bool> RepairInvalidOhlc(CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(_scraperOptions.OhlcRepairBatchSize, 0);
        if (batchSize == 0)
            return true;

        List<InvalidOhlcTarget> targets;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
            targets = await repo.GetAllSeries()
                .AsNoTracking()
                .Where(p =>
                    (
                        p.ListedTicker == p.CommonStock.Ticker
                        || p.CommonStock.SecondaryTickers.Contains(p.ListedTicker)
                    )
                    && (
                        p.Open <= 0
                        || p.High <= 0
                        || p.Low <= 0
                        || p.Close <= 0
                        || p.High < p.Open
                        || p.High < p.Close
                        || p.Low > p.Open
                        || p.Low > p.Close
                        || p.High < p.Low
                    )
                )
                .OrderBy(p => p.CreationTime)
                .ThenBy(p => p.Id)
                .Take(batchSize)
                .Select(p => new InvalidOhlcTarget(p.Id, p.CommonStockId, p.ListedTicker, p.Date))
                .ToListAsync(cancellationToken);
        }

        if (targets.Count == 0)
            return true;

        var replacements = new Dictionary<Guid, DailyStockPrice>();
        var deferredSeries = new HashSet<PriceSeriesKey>();

        foreach (var group in targets.GroupBy(t => new { t.CommonStockId, t.Ticker }))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(group.Key.Ticker))
                continue;

            try
            {
                var startDate = group.Min(t => t.Date);
                var endDate = group.Max(t => t.Date);
                var chartData = await _yahooClient.GetChart(group.Key.Ticker, startDate, endDate);
                var fetchedByDate = MapFreshRows(
                        group.Key.CommonStockId,
                        chartData.Prices,
                        group.Key.Ticker,
                        endDate.AddDays(1)
                    )
                    .GroupBy(p => p.Date)
                    .ToDictionary(g => g.Key, g => g.Last());

                foreach (var target in group)
                {
                    if (fetchedByDate.TryGetValue(target.Date, out var replacement))
                        replacements[target.PriceId] = replacement;
                }
            }
            catch (HttpRequestException ex)
            {
                deferredSeries.Add(new PriceSeriesKey(group.Key.CommonStockId, group.Key.Ticker));
                _logger.LogWarning(
                    ex,
                    "Failed to fetch OHLC repair data for {Ticker}; deferring its invalid rows",
                    group.Key.Ticker
                );
            }
        }

        var repaired = 0;
        var removed = 0;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
            var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
            await using var transaction = await repo.CreateTransaction(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
            var targetIds = targets.Select(t => t.PriceId).ToList();
            var seriesByPriceId = targets.ToDictionary(
                target => target.PriceId,
                target => new PriceSeriesKey(target.CommonStockId, target.Ticker)
            );
            var validSeries = new HashSet<PriceSeriesKey>();
            foreach (
                var series in seriesByPriceId
                    .Values.Distinct()
                    .OrderBy(series => series.CommonStockId)
                    .ThenBy(series => series.ListedTicker, StringComparer.Ordinal)
            )
            {
                var target = new PriceSeriesTarget(
                    series.ListedTicker,
                    series.CommonStockId,
                    IsPrimary: false
                );
                if (await LockPriceSeries(stockRepo, target, cancellationToken) != null)
                    validSeries.Add(series);
            }
            var storedRows = await repo.GetAllSeries()
                .Where(p => targetIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var row in storedRows)
            {
                var series = seriesByPriceId[row.Id];
                if (deferredSeries.Contains(series) || !validSeries.Contains(series))
                    continue;

                if (
                    replacements.TryGetValue(row.Id, out var replacement)
                    && IsSameSplitBasis(row.Close, replacement.Close)
                )
                {
                    row.Open = replacement.Open;
                    row.High = replacement.High;
                    row.Low = replacement.Low;
                    row.Close = replacement.Close;
                    if (IsVolumeUpgrade(row.Volume, replacement.Volume))
                        row.Volume = replacement.Volume;
                    repaired++;
                }
                else
                {
                    repo.Delete(row);
                    removed++;
                }
            }

            if (repaired > 0 || removed > 0)
                await repo.SaveChanges();
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Historical OHLC repair processed {Count} rows: {Repaired} repaired, {Removed} removed, {Deferred} deferred",
            targets.Count,
            repaired,
            removed,
            deferredSeries.Count
        );

        return targets.Count < batchSize && deferredSeries.Count == 0;
    }

    private sealed record InvalidOhlcTarget(
        Guid PriceId,
        Guid CommonStockId,
        string Ticker,
        DateOnly Date
    );

    // Settled volume only ever accrues, so a fetched figure below the stored one is a degraded
    // response (a partial re-serve, a venue dropping out), never a correction. Accepting only
    // upgrades makes the repair monotone: a flaky feed can never walk a good figure back down.
    private static bool IsVolumeUpgrade(long stored, long fetched) => fetched > stored;

    // Relative half-width of the same-basis close comparison; full rationale on IsSameSplitBasis.
    private const decimal SameBasisCloseTolerance = 0.01m;

    // One last-digit tick of absolute headroom on top of the relative tolerance. Both closes are
    // rounded to 4 decimals at ingest, so a genuine minor revision of a sub-cent close moves it by
    // a full 0.0001 — more than 1% of the price — and a purely relative tolerance would freeze the
    // resettle out of the OTC tail. One tick stays orders of magnitude below any split ratio.
    private const decimal SameBasisCloseTickHeadroom = 0.0001m;

    // Two records of the same session are only comparable when they are on the same split basis,
    // and the close is what proves it: a split moves price and volume by the SAME ratio in
    // opposite directions, so a basis mismatch shows up as a close that differs by that ratio.
    //
    // The stored series and the feed genuinely disagree here, in BOTH orderings — the guard must
    // stay direction-agnostic:
    //  - Pre-reconcile (the window EVERY split passes through): CaptureSplits records a split at
    //    the end of the same cycle whose ReconcilePendingCorporateActions pass already ran, so until the
    //    next cycle the stored pre-split rows are still as-traded while the feed already serves
    //    them adjusted. On a forward split the adjusted volume is ratio-times LARGER, so it reads
    //    as a settlement upgrade and would leave a row whose volume is adjusted under an as-traded
    //    close.
    //  - Post-reconcile (observed on WLFC's 3:1): the reconcile stored the adjusted basis and the
    //    feed later went back to serving the window as-traded. On a reverse split the as-traded
    //    volume is ratio-times larger than the stored adjusted one, so it reads as an upgrade and
    //    would inflate the stock's volume history by the split ratio.
    // Which basis each side holds varies by stock and over time (PRPL's reconciled series is
    // as-traded while WLFC's is adjusted, minutes apart), so only this value comparison is safe —
    // a split-table lookup would guess wrong on real data. A mismatch means skip, never rewrite:
    // volume basis belongs to the split reconcile, which rewrites the series as a whole.
    //
    // Tolerance: both closes are rounded to 4 decimals at ingest, so same-basis values differ only
    // by a genuine minor revision — well inside 1% — while the split ratios Yahoo emits for real
    // splits (5:4 = 25%, 21:20 = 4.76%) sit far outside it. The one family inside the tolerance is
    // a tiny stock dividend recorded as a split (101:100 = 0.99%); accepting it bounds the volume
    // error at ~1%, negligible against the 10-29% unsettled shortfall the resettle exists to fix.
    private static bool IsSameSplitBasis(decimal storedClose, decimal fetchedClose)
    {
        // Nothing to compare against, so the basis is unproven rather than matching — and a zero
        // stored close would collapse the relative tolerance to exact equality.
        if (storedClose <= 0m || fetchedClose <= 0m)
            return false;

        return Math.Abs(fetchedClose - storedClose)
            <= storedClose * SameBasisCloseTolerance + SameBasisCloseTickHeadroom;
    }

    // The oldest date whose stored volume is still re-read. Pure so the boundary is pinnable, and
    // clamped so a zero or negative setting degrades to "today only" — which the settled-bar guard
    // then empties — rather than reaching back over the whole series.
    private static DateOnly ResettleWindowStart(DateOnly today, int windowDays) =>
        today.AddDays(-Math.Max(windowDays, 0));

    // Upserts the split events Yahoo returned for this ticker into StockSplit via
    // the CorporateActions capture manager. Resolved in its own scope (mirrors
    // the other per-write scopes); skipped when there are no splits so the common
    // no-split path costs nothing.
    private async Task CaptureSplits(
        PriceSeriesTarget target,
        IReadOnlyCollection<StockSplitEvent> splits,
        CancellationToken cancellationToken
    )
    {
        if (splits.Count == 0)
            return;

        // Map Yahoo's split shape onto the source-neutral capture DTO at the
        // worker boundary, stamping Yahoo as the source, so the domain manager
        // stays decoupled from this integration.
        var captured = splits
            .Select(s => new CapturedSplit
            {
                EffectiveDate = s.Date,
                Numerator = s.Numerator,
                Denominator = s.Denominator,
                Source = StockSplitSource.Yahoo,
            })
            .ToList();

        using var scope = _scopeFactory.CreateScope();
        var captureManager = scope.ServiceProvider.GetRequiredService<StockSplitCaptureManager>();
        var count = await captureManager.Capture(
            target.CommonStockId,
            target.Ticker,
            captured,
            cancellationToken
        );
        if (count > 0)
            _logger.LogInformation(
                "Captured {Count} stock split(s) for {Ticker} on {StockId}",
                count,
                target.Ticker,
                target.CommonStockId
            );
    }

    // Upserts the dividend events Yahoo returned for this ticker into
    // CashDividend via the CorporateActions capture manager. Mirrors
    // CaptureSplits: its own scope, and skipped when there are no dividends so
    // the common no-dividend path costs nothing.
    private async Task<IReadOnlyCollection<CapturedDividend>> CaptureDividends(
        PriceSeriesTarget target,
        IReadOnlyCollection<CashDividendEvent> dividends,
        CancellationToken cancellationToken
    )
    {
        if (dividends.Count == 0)
            return [];

        // Map Yahoo's dividend shape onto the source-neutral capture DTO at the
        // worker boundary, stamping Yahoo as the source, so the domain manager
        // stays decoupled from this integration.
        var captured = dividends
            .Select(d => new CapturedDividend
            {
                ExDate = d.Date,
                AmountPerShare = d.Amount,
                Source = CashDividendSource.Yahoo,
            })
            .ToList();

        using var scope = _scopeFactory.CreateScope();
        var captureManager = scope.ServiceProvider.GetRequiredService<CashDividendCaptureManager>();
        var count = await captureManager.Capture(
            target.CommonStockId,
            target.Ticker,
            captured,
            cancellationToken
        );
        if (count > 0)
            _logger.LogInformation(
                "Captured {Count} cash dividend(s) for {Ticker} on {StockId}",
                count,
                target.Ticker,
                target.CommonStockId
            );

        return captured;
    }

    private async Task FlushPriceBatch(List<DailyStockPrice> batch)
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        await using var transaction = await repo.CreateTransaction(IsolationLevel.ReadCommitted);

        // Each batch holds rows for one exact ticker. Locking the parent makes ownership
        // validation and insert atomic with a concurrent CompanySync designation change.
        var first = batch[0];
        var target = new PriceSeriesTarget(
            first.ListedTicker,
            first.CommonStockId,
            IsPrimary: false
        );
        if (await LockPriceSeries(stockRepo, target, CancellationToken.None) == null)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(
                "Skipping {Count} prices for {Ticker}: it no longer belongs to CommonStock {Id}",
                batch.Count,
                first.ListedTicker,
                first.CommonStockId
            );
            return;
        }

        // Another price writer can add a date after PersistPrices' optimistic read and before
        // this series lock. Rechecking while locked turns that collision into an idempotent skip
        // instead of rolling back every later row in this batch.
        var batchDates = batch.Select(price => price.Date).ToList();
        var existingDates = await repo.GetAllSeries()
            .Where(p =>
                p.CommonStockId == first.CommonStockId
                && p.ListedTicker == first.ListedTicker
                && batchDates.Contains(p.Date)
            )
            .Select(p => p.Date)
            .ToListAsync();
        batch.RemoveAll(price => existingDates.Contains(price.Date));
        if (batch.Count == 0)
        {
            await transaction.CommitAsync();
            return;
        }

        repo.AddRange(batch);
        await repo.SaveChanges();
        await transaction.CommitAsync();
    }

    private async Task SyncKeyStatistics(
        PriceSeriesTarget target,
        CancellationToken cancellationToken
    )
    {
        var ticker = target.Ticker;
        // Yahoo has NOTHING for some listings (closed-end funds like PSUS, fresh IPOs): no stats
        // modules at all, or every field zero. That used to end the sync, leaving the stored pair
        // at 0/0 forever — even when EDGAR carries an authoritative cover-page count and this same
        // cycle just stored a close to price it. Substitute an empty stats object and fall
        // through: the EDGAR share count still lands, the market cap falls back to shares × the
        // latest stored close (the #5238 branch), and a ticker with no EDGAR anchor either writes
        // nothing, exactly as before (every write below is conditional).
        var stats = await _yahooClient.GetKeyStatistics(ticker) ?? new KeyStatistics();

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        await using var transaction = await stockRepo.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var lockedSeries = await LockPriceSeries(stockRepo, target, cancellationToken);
        if (lockedSeries is not { IsPrimary: true })
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Skipping key statistics for {Ticker}: it is no longer the primary listing on CommonStock {Id}",
                ticker,
                target.CommonStockId
            );
            return;
        }
        var stock = lockedSeries.Value.Stock;

        // The SEC cover-page count (dei:EntityCommonStockSharesOutstanding) is authoritative and
        // current; Yahoo's figure is per-share-class and lags corporate actions. Defer to EDGAR
        // for the share count when the issuer has an SEC fact, so Yahoo can't overwrite it with a
        // stale or single-class value (#3575/#2503). Uses the more-recently-filed of the
        // consolidated and per-class facts so a dual-class issuer frozen on a stale consolidated
        // value falls through to its current per-class total (#5158).
        var sharesProvider = scope.ServiceProvider.GetRequiredService<ISharesOutstandingProvider>();
        var edgarShares = await sharesProvider.GetCurrentSharesOutstanding(
            stock,
            cancellationToken
        );

        // A foreign private issuer (20-F/40-F filer) reports its cover-page count in ordinary
        // shares, a different unit from the US-listed ADR Yahoo prices. Yahoo returns the correct
        // market cap and the share base it was built on for the ticker, so reconciling it onto
        // the EDGAR ordinary base would inflate market cap by the ADR ratio (e.g. Latam Airlines
        // $16.7B -> $33T at ~2000x). Drop the EDGAR count for these issuers so Yahoo's figures
        // stand verbatim; the reconciliation stays in force for domestic 10-K/10-Q filers.
        if (
            edgarShares != null
            && await sharesProvider.IsForeignPrivateIssuer(stock, cancellationToken)
        )
            edgarShares = null;

        // The form-based guard above can't see a DOMESTIC filer whose US listing is still an ADS:
        // a company that lost foreign-private-issuer status files 10-K/10-Q while its cover page
        // keeps counting ordinary shares. So ask what it LISTED rather than what it files. The
        // 12(b) registration title for the stock's own ticker is already materialized on the
        // stock from that same cover page ("American Depositary Shares, each representing 13
        // Ordinary Shares"), and a depositary receipt is a different unit from the ordinary
        // shares counted beside it — so drop the EDGAR count exactly like the FPI path.
        //
        // This has to run BEFORE the ratio guard below and cannot be left to it: real deposit
        // ratios are small (ONC 13x, SNY and AZN 2x) and sit far inside the band where two counts
        // are still credible statements of the same unit, so the figures alone can never expose
        // them. ONC stored a $557B market cap against a true ~$42.9B for exactly this reason, and
        // the damage is invisible downstream because the stored pair stays self-consistent
        // (cap ÷ shares == the close). The ratio itself is never read out of the title — only the
        // fact that the listing is a receipt — so the repair is to stop rescaling, not to divide.
        if (
            edgarShares != null
            && ListedSecurityClassifier.IsAmericanDepositary(stock.ListedSecurityTitle)
        )
            edgarShares = null;

        // A last guard for issuers the two authoritative statements above miss: detect the unit
        // mismatch from the figures themselves, when the EDGAR count and Yahoo's own share base
        // are too far apart to be statements of the same unit. This catches an ADS issuer with no
        // registered title on record (AKTX, 80,000 ordinary per ADS) and stops a garbage EDGAR
        // count (ABTC 458x, CNDA 768x off any real basis) from poisoning the rescale. The
        // threshold is deliberately far above any corporate-action lag (see
        // MaxPlausibleSameUnitRatio), so a lagging reverse split — where EDGAR is right and the
        // rescale must proceed (#3575) — cannot trip it.
        var yahooShareBase = YahooShareBase(stats);
        if (
            edgarShares is > 0
            && ShareBasisPlausibility.IsUnitMismatch(edgarShares.Value, yahooShareBase)
        )
            edgarShares = null;

        // Per-field conservative writes: only update on actual change, and never
        // overwrite a known value with 0 (treated as Yahoo "unknown" by the rest of
        // the codebase).
        //
        // Without an EDGAR base the stored pair comes entirely from Yahoo, and the share count
        // must be the base Yahoo built its market cap on (impliedSharesOutstanding when provided
        // — see YahooShareBase), NOT the quoted-listing sharesOutstanding. For a foreign ADR or
        // OTC ordinary Yahoo's market cap is the full-company figure while sharesOutstanding
        // counts only the US listing, so storing that count leaves the derived price
        // (cap ÷ shares) off by the ADR ratio / listing mix (CYATY 21x, SNHIY 12.8x, JHPCY 26x).
        var changed = false;
        if (edgarShares == null && yahooShareBase != 0 && stock.SharesOutStanding != yahooShareBase)
        {
            stock.SharesOutStanding = yahooShareBase;
            changed = true;
        }
        // When the EDGAR count is the authoritative base (not dropped above), store it here too —
        // not only in the financial-facts importer — so the share count and the market cap
        // rescaled onto it below always land together and the stored pair is never split across
        // two bases between worker cycles. This is also the arbiter behind the facts importer's
        // own unit-mismatch guard: that guard refuses to overwrite a stored count that is credibly
        // on the listed-security basis, and when such a refusal goes stale (a large legitimate
        // issuance moved the true count), it is corrected here, where Yahoo's agreeing share base
        // proves the EDGAR count plausible.
        if (edgarShares is > 0 && stock.SharesOutStanding != edgarShares.Value)
        {
            stock.SharesOutStanding = edgarShares.Value;
            changed = true;
        }
        // Reconcile Yahoo's market cap onto the authoritative EDGAR share base so it never
        // disagrees with SharesOutStanding by the share-count ratio (#3575/#2503). When Yahoo's own
        // market cap is unusable, fall back to EDGAR shares × the latest stored close (#5238) —
        // otherwise a corrected SharesOutStanding never gets a matching MarketCapitalization.
        decimal? currentPrice = null;
        if (stats.MarketCapitalization == 0 && edgarShares is > 0)
        {
            var priceRepo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
            currentPrice = await priceRepo
                .GetTradedByStock(stock)
                .OrderByDescending(p => p.Date)
                .Select(p => (decimal?)p.Close)
                .FirstOrDefaultAsync(cancellationToken);
        }
        var marketCap = ReconcileMarketCap(
            edgarShares,
            stats.SharesOutstanding,
            stats.ImpliedSharesOutstanding,
            stats.MarketCapitalization,
            currentPrice
        );
        if (marketCap != 0 && stock.MarketCapitalization != marketCap)
        {
            stock.MarketCapitalization = marketCap;
            changed = true;
        }

        if (!changed)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        if (!await SaveStockChanges(stockRepo, target.CommonStockId, ticker, cancellationToken))
            return;
        await transaction.CommitAsync(cancellationToken);

        _logger.LogDebug(
            "Updated key stats for {Ticker}: shares={Shares} marketCap={MarketCap}",
            ticker,
            stats.SharesOutstanding,
            stats.MarketCapitalization
        );
    }

    // When EDGAR is the authoritative share source the importer keeps EDGAR's SharesOutStanding,
    // so storing Yahoo's market cap verbatim leaves the two figures on different share bases —
    // they disagree by the share-count ratio (a reverse-split lag inflates market cap ~20x, COPR
    // #3575; a multi-class issuer understates Yahoo's shares ~2x, #2503). Rescale Yahoo's market
    // cap onto the EDGAR base (== EDGAR shares × the same implied price) so market cap stays
    // consistent with SharesOutStanding and the screener's derived price (market cap ÷ shares)
    // holds. Falls back to Yahoo's figure when there is no EDGAR count or no usable Yahoo share
    // base to rescale from. The caller passes edgarShares == null whenever the EDGAR count is in a
    // different unit from the listing Yahoo prices — a foreign private issuer (20-F/40-F) or a
    // domestic filer whose registered 12(b) title says it listed American Depositary Shares, both
    // of which count ordinary shares on the cover page, and any issuer whose EDGAR count and
    // Yahoo share base are too far apart to be statements of the same unit at all (see
    // ShareBasisPlausibility). Those keep Yahoo's self-consistent listed-security market cap
    // rather than being rescaled onto the ordinary base.
    //
    // The rescale must divide by the share base Yahoo actually built its market cap on. That is
    // impliedSharesOutstanding (the entity-wide count, all classes) when Yahoo provides it — NOT
    // sharesOutstanding, which covers only the quoted class. Dividing a full-company market cap
    // by a single-class count inflates every multi-class issuer by the class ratio (GOOGL stored
    // 9.23T against a true ~4.4T, DELL ~2x, UHAL ~10x). Only when Yahoo omits the implied count
    // is sharesOutstanding assumed to be the base, which keeps the #3575 reverse-split correction:
    // there the whole Yahoo quote lags the split, so cap ÷ (either stale base) × EDGAR shares
    // still lands on EDGAR shares × price.
    //
    // Yahoo sometimes returns no market cap at all (summaryDetail.marketCap missing — common for
    // multi-class issuers it hasn't reconciled, #5238): with no Yahoo market cap there is nothing
    // to rescale, and the figure would otherwise stay stale forever even after EDGAR's share count
    // is corrected. When a current price is available (the same import cycle's freshly-fetched
    // close), compute EDGAR shares × price directly instead of leaving the stored value untouched.
    private static double ReconcileMarketCap(
        long? edgarShares,
        long yahooShares,
        long yahooImpliedShares,
        double yahooMarketCap,
        decimal? currentPrice = null
    )
    {
        var yahooShareBase = YahooShareBase(yahooImpliedShares, yahooShares);
        if (edgarShares is > 0 && yahooShareBase > 0 && yahooMarketCap > 0)
            return yahooMarketCap * ((double)edgarShares.Value / yahooShareBase);
        if (edgarShares is > 0 && currentPrice is > 0)
            return (double)edgarShares.Value * (double)currentPrice.Value;
        return yahooMarketCap;
    }

    private static long YahooShareBase(KeyStatistics stats) =>
        YahooShareBase(stats.ImpliedSharesOutstanding, stats.SharesOutstanding);

    // The share base Yahoo built its published market cap on: the entity-wide implied count when
    // provided, else the quoted-class count. The single definition shared by the unit-mismatch
    // guard and ReconcileMarketCap — the base the guard vets must always be the base the rescale
    // divides by, or a mismatch could be vetted against one figure and rescaled from another.
    private static long YahooShareBase(long yahooImpliedShares, long yahooShares) =>
        yahooImpliedShares > 0 ? yahooImpliedShares : yahooShares;

    private async Task SyncCompanyProfile(
        PriceSeriesTarget target,
        CancellationToken cancellationToken
    )
    {
        var ticker = target.Ticker;
        var profile = await _yahooClient.GetCompanyProfile(ticker);
        if (profile == null || string.IsNullOrWhiteSpace(profile.Industry))
            return;

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var industryRepo = scope.ServiceProvider.GetRequiredService<IndustryRepository>();
        var sectorRepo = scope.ServiceProvider.GetRequiredService<SectorRepository>();
        await using var transaction = await stockRepo.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var lockedSeries = await LockPriceSeries(stockRepo, target, cancellationToken);
        if (lockedSeries is not { IsPrimary: true })
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Skipping company profile for {Ticker}: it is no longer the primary listing on CommonStock {Id}",
                ticker,
                target.CommonStockId
            );
            return;
        }
        var stock = lockedSeries.Value.Stock;

        // Upsert by case-insensitive name. Yahoo uses a small stable vocabulary, so
        // collisions are rare and a flat scan over Sector/Industry is fine — both tables
        // hold tens of rows at steady state. Materialize the lookup once per call.
        var sectorId = await UpsertSectorIfPresent(sectorRepo, profile.Sector, cancellationToken);
        var industry = await UpsertIndustry(
            industryRepo,
            profile.Industry,
            sectorId,
            cancellationToken
        );

        if (stock.IndustryId == industry.Id)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        stock.IndustryId = industry.Id;
        if (!await SaveStockChanges(stockRepo, target.CommonStockId, ticker, cancellationToken))
            return;
        await transaction.CommitAsync(cancellationToken);

        _logger.LogDebug(
            "Updated industry for {Ticker}: {Industry} (sector {Sector})",
            ticker,
            profile.Industry,
            profile.Sector ?? "?"
        );
    }

    private async Task<bool> SaveStockChanges(
        CommonStockRepository stockRepo,
        Guid commonStockId,
        string ticker,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await stockRepo.SaveChanges();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            var stillExists = await stockRepo
                .GetAll()
                .AsNoTracking()
                .AnyAsync(s => s.Id == commonStockId, cancellationToken);
            if (stillExists)
                throw;

            _logger.LogWarning(
                "Skipping enrichment save for {Ticker}: CommonStock {Id} was removed during the write",
                ticker,
                commonStockId
            );
            return false;
        }
    }

    private static async Task<Guid?> UpsertSectorIfPresent(
        SectorRepository sectorRepo,
        string sectorName,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(sectorName))
            return null;

        var existing = await sectorRepo
            .GetAll()
            .FirstOrDefaultAsync(s => s.Name.ToLower() == sectorName.ToLower(), cancellationToken);
        if (existing != null)
            return existing.Id;

        var sector = new Equibles.CommonStocks.Data.Models.Taxonomies.Sector { Name = sectorName };
        sectorRepo.Add(sector);
        await sectorRepo.SaveChanges();
        return sector.Id;
    }

    private static async Task<Equibles.CommonStocks.Data.Models.Taxonomies.Industry> UpsertIndustry(
        IndustryRepository industryRepo,
        string industryName,
        Guid? sectorId,
        CancellationToken cancellationToken
    )
    {
        var existing = await industryRepo
            .GetAll()
            .FirstOrDefaultAsync(
                i => i.Name.ToLower() == industryName.ToLower(),
                cancellationToken
            );
        if (existing != null)
        {
            // Backfill the sector link if it was missing — newly-imported industries that
            // pre-dated the Sector taxonomy would otherwise stay unlinked. An already-linked
            // industry keeps its existing sector even when Yahoo classifies it differently.
            if (sectorId.HasValue && !existing.SectorId.HasValue)
            {
                existing.SectorId = sectorId;
                await industryRepo.SaveChanges();
            }
            return existing;
        }

        var industry = new Equibles.CommonStocks.Data.Models.Taxonomies.Industry
        {
            Name = industryName,
            SectorId = sectorId,
        };
        industryRepo.Add(industry);
        await industryRepo.SaveChanges();
        return industry;
    }

    // The forward-only start date, pulled back to cover a recent settled session that is missing
    // from the stored series (see GapHealWindowDays). Only a stock that actually has a hole widens
    // its window, so an up-to-date stock still costs zero Yahoo calls — the property the whole
    // cheap-cycle design rests on.
    private async Task<DateOnly> ResolveStartDate(
        PriceSeriesTarget target,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        if (target.RequiresFullHistory)
            return PriceHistoryFloor();

        var forwardOnly = await GetSyncStartDate(target, cancellationToken);

        // The heal only ever RIDES a fetch the forward-only date already demands — it must never
        // trigger one of its own. A stock that is fully current returns here untouched, so the
        // "current stocks cost zero Yahoo calls" property the whole cheap-cycle design rests on
        // survives, and so does its DB twin (no extra window query on quiet cycles). Without this
        // gate a holed-but-otherwise-current stock re-fetched on EVERY cycle for the life of the
        // window: for the 2026-07-24 upstream outage that is ~5,500 stocks × ~11 quiet cycles a
        // day against the shared request budget — a ten-day self-inflicted fetch storm. The same
        // gate bounds two structural cases to one attempt per settled session, riding a fetch that
        // was happening anyway: a thin stock that simply does not trade every session (whose
        // rolling window always contains "holes"), and an unmodeled market-wide closure (a
        // mourning day / weather closure UsMarketCalendar does not list), which holes the entire
        // universe at once.
        if (!HasSettledTradingDay(forwardOnly, today))
            return forwardOnly;

        // Past the same gate, pull the start back over the resettle window so the response carries
        // the recently-stored bars whose OHLC/volume may not have settled yet (see
        // ResettleStoredBars). Same ride-along rule as the heal below: it widens a request that was
        // already being made, never triggers one, so an up-to-date stock still costs zero calls.
        var startDate = Min(
            forwardOnly,
            ResettleWindowStart(today, _scraperOptions.VolumeResettleWindowDays)
        );

        var windowStart = today.AddDays(-GapHealWindowDays);
        // Already reaching back past the window (a never-synced stock, one mid-backfill, or a
        // resettle window widened past the heal window) — it is going to re-request those sessions
        // anyway, so there is nothing to widen.
        if (startDate <= windowStart)
            return startDate;

        List<DateOnly> storedDates;
        DateOnly? earliestStored;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
            storedDates = await repo.GetAllSeries()
                .Where(p =>
                    p.CommonStockId == target.CommonStockId
                    && p.ListedTicker == target.Ticker
                    && p.Date >= windowStart
                    && p.Date < today
                )
                .Select(p => p.Date)
                .ToListAsync(cancellationToken);
            // The stock's first bar EVER, not first-in-window: the discriminator between "listed
            // mid-window" and "the feed failed to serve the window's leading edge" (see
            // FindEarliestGap). An aggregate on the (CommonStockId, Date) index.
            earliestStored = await repo.GetAllSeries()
                .Where(p =>
                    p.CommonStockId == target.CommonStockId && p.ListedTicker == target.Ticker
                )
                .MinAsync(p => (DateOnly?)p.Date, cancellationToken);
        }

        var earliestGap = FindEarliestGap(
            storedDates,
            windowStart,
            today,
            hasHistoryBeforeWindow: earliestStored < windowStart
        );
        return earliestGap is { } gap && gap < startDate ? gap : startDate;
    }

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;

    // The earliest settled trading day in [windowStart, today) with no stored bar, or null when the
    // window is complete. Pure so the rule is pinnable without a database.
    //
    // The scan's starting point decides two opposite cases. A stock with bars OLDER than the window
    // has provably existed for all of it, so a missing session at the window's leading edge is a
    // real hole — scanning from the earliest IN-WINDOW bar instead would silently shorten the heal
    // window as an outage day slides toward its edge. A stock whose entire history STARTS inside
    // the window (a new listing) scans from its first bar, because the days before a listing
    // existed are not holes. A stock with nothing stored in the window at all has no gap to speak
    // of — that is plain staleness, which the forward-only start date already covers.
    private static DateOnly? FindEarliestGap(
        List<DateOnly> storedDates,
        DateOnly windowStart,
        DateOnly today,
        bool hasHistoryBeforeWindow
    )
    {
        if (storedDates.Count == 0)
            return null;

        var stored = storedDates.ToHashSet();
        var scanFrom = hasHistoryBeforeWindow ? windowStart : storedDates.Min();

        for (var date = scanFrom; date < today; date = date.AddDays(1))
        {
            if (UsMarketCalendar.IsTradingDay(date) && !stored.Contains(date))
                return date;
        }

        return null;
    }

    private async Task<DateOnly> GetSyncStartDate(
        PriceSeriesTarget target,
        CancellationToken cancellationToken
    )
    {
        return await SyncStartDate.Resolve<DailyStockPriceRepository>(
            _scopeFactory,
            _workerOptions,
            repo =>
                repo.GetAllSeries()
                    .Where(p =>
                        p.CommonStockId == target.CommonStockId && p.ListedTicker == target.Ticker
                    )
                    .Select(p => p.Date)
                    .OrderByDescending(d => d),
            cancellationToken
        );
    }

    private HashSet<DateOnly> WarnAndCollectOverflowDates(
        List<HistoricalPrice> prices,
        string ticker
    )
    {
        var outOfRange = prices.Where(HasOverflowPrice).ToList();
        if (outOfRange.Count > 0)
        {
            var sample = outOfRange[0];
            _logger.LogWarning(
                "Skipping {Count} prices for {Ticker} exceeding numeric(18,4) limit. "
                    + "Sample: {Date} O={Open} H={High} L={Low} C={Close} AC={AdjClose}",
                outOfRange.Count,
                ticker,
                sample.Date,
                sample.Open,
                sample.High,
                sample.Low,
                sample.Close,
                sample.AdjustedClose
            );
        }

        return outOfRange.Select(p => p.Date).ToHashSet();
    }

    private HashSet<DateOnly> WarnAndCollectInvalidOhlcDates(
        List<HistoricalPrice> prices,
        string ticker
    )
    {
        var invalid = prices.Where(p => IsInvalidOhlc(p)).ToList();
        if (invalid.Count > 0)
        {
            var sample = invalid[0];
            _logger.LogWarning(
                "Skipping {Count} prices for {Ticker} with impossible OHLC. "
                    + "Sample: {Date} O={Open} H={High} L={Low} C={Close}",
                invalid.Count,
                ticker,
                sample.Date,
                sample.Open,
                sample.High,
                sample.Low,
                sample.Close
            );
        }

        return invalid.Select(p => p.Date).ToHashSet();
    }

    private static bool IsInvalidOhlc(HistoricalPrice price) =>
        price.Open <= 0
        || price.High <= 0
        || price.Low <= 0
        || price.Close <= 0
        || price.High < price.Open
        || price.High < price.Close
        || price.Low > price.Open
        || price.Low > price.Close
        || price.High < price.Low;

    private static bool HasOverflowPrice(HistoricalPrice p) =>
        Math.Abs(p.Open) > MaxPriceValue
        || Math.Abs(p.High) > MaxPriceValue
        || Math.Abs(p.Low) > MaxPriceValue
        || Math.Abs(p.Close) > MaxPriceValue
        || Math.Abs(p.AdjustedClose) > MaxPriceValue;

    private async Task<HashSet<DateOnly>> GetExistingDates(
        PriceSeriesTarget target,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();

        var dates = await repo.GetAllSeries()
            .Where(p =>
                p.CommonStockId == target.CommonStockId
                && p.ListedTicker == target.Ticker
                && p.Date >= startDate
                && p.Date <= endDate
            )
            .Select(p => p.Date)
            .ToListAsync(cancellationToken);

        return dates.ToHashSet();
    }
}
