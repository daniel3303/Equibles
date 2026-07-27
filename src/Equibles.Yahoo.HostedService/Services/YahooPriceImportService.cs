using System.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Calendars;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Worker;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Yahoo.HostedService.Services;

[Service]
public class YahooPriceImportService
{
    private const int InsertBatchSize = 500;
    private const decimal MaxPriceValue = 99_999_999_999_999.9999m; // numeric(18,4) ceiling

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<YahooPriceImportService> _logger;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly TickerMapService _tickerMapService;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;

    public YahooPriceImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<YahooPriceImportService> logger,
        IYahooFinanceClient yahooClient,
        TickerMapService tickerMapService,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _yahooClient = yahooClient;
        _tickerMapService = tickerMapService;
        _errorReporter = errorReporter;
        _workerOptions = workerOptions.Value;
    }

    public Task Import(CancellationToken cancellationToken) =>
        Import(includeEnrichment: true, cancellationToken);

    /// <summary>
    /// One price-sync cycle over the tracked universe. With <paramref name="includeEnrichment"/>
    /// false only the incremental chart fetch runs per stock (1 Yahoo call, and none at all for a
    /// stock that is already current — see the settled-trading-day gate in ImportTicker), so the
    /// worker can run frequent cheap price cycles and reserve the key-statistics + company-profile
    /// calls (2 extra Yahoo calls per stock, the bulk of a cycle's traffic) for a slower cadence.
    /// </summary>
    public async Task Import(bool includeEnrichment, CancellationToken cancellationToken)
    {
        var tickerMap = await _tickerMapService.Build(
            _workerOptions.TickersToSync,
            cancellationToken
        );
        _logger.LogInformation(
            "Starting Yahoo price sync for {Count} stocks (enrichment: {Enrichment})",
            tickerMap.Count,
            includeEnrichment ? "on" : "off"
        );

        // Before the forward-only incremental append: reconcile any stock whose stored history is
        // on a pre-split basis. GetSyncStartDate only appends, so a split that landed after the
        // last sync leaves the old rows on the wrong basis (a discontinuity in the series). Re-pull
        // those stocks' full, fully-adjusted history and overwrite the stored rows (#2879).
        await ReconcilePendingSplits(DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

        // Crawl the recently-active stocks first, stalest-first within them, and the long-dormant
        // tail afterwards (see OrderByCrawlPriority for why the plain stalest-first order starved
        // the daily lane).
        var crawlOrder = await OrderByCrawlPriority(tickerMap, cancellationToken);

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
            await ImportEnrichment(crawlOrder, cancellationToken);
    }

    // Pass 1 — the settled daily bars. One Yahoo call per stock that actually needs one.
    private async Task<int> ImportPrices(
        List<KeyValuePair<string, Guid>> crawlOrder,
        CancellationToken cancellationToken
    )
    {
        var totalInserted = 0;
        var fetched = 0;
        var fetchedWithNothingNew = 0;

        foreach (var (ticker, commonStockId) in crawlOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Resolved per ticker, not once per cycle: a multi-hour crawl straddles the UTC
            // midnight rollover, and a cycle-start snapshot would keep excluding the just-settled
            // bar for every stock processed after midnight — a cycle starting 23:50 UTC used to
            // ship a whole day late for the entire universe.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            try
            {
                var result = await ImportTicker(ticker, commonStockId, today, cancellationToken);
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

        WarnIfUpstreamServedNothing(fetched, fetchedWithNothingNew);
        return totalInserted;
    }

    // A stock is only fetched when a settled session is genuinely missing for it, so a fetch that
    // returns nothing new is the upstream feed failing to serve a bar it should have. One or two of
    // those is ordinary (a stock that did not trade); the whole crawl doing it is an outage.
    //
    // This exists because that outage is otherwise INVISIBLE. On 2026-07-24 Yahoo served the entire
    // session's daily bars with null OHLC; the importer correctly refused to store them, so the lane
    // ran flat out — thousands of successful HTTP 200s — and wrote nothing, with every log line at
    // Information saying the cycle had started and completed normally. The per-fetch detail that
    // would have shown it is at Debug, which production does not emit.
    private void WarnIfUpstreamServedNothing(int fetched, int fetchedWithNothingNew)
    {
        if (fetched < MinFetchesForUpstreamWarning)
            return;

        var barrenRatio = (double)fetchedWithNothingNew / fetched;
        if (barrenRatio < BarrenFetchWarningRatio)
            return;

        _logger.LogWarning(
            "Yahoo served no new settled bars for {Barren} of {Fetched} stocks that needed one "
                + "({Percent:P0}). The price feed is likely publishing incomplete bars upstream; "
                + "stored prices will stay stale until it recovers.",
            fetchedWithNothingNew,
            fetched,
            barrenRatio
        );
    }

    // Small crawls (a weekend no-op, a tiny configured universe) are not evidence of anything.
    private const int MinFetchesForUpstreamWarning = 200;

    // Deliberately high: a normal catch-up cycle inserts for nearly every stock it fetches, so this
    // only trips when the feed is broadly refusing to serve usable bars.
    private const double BarrenFetchWarningRatio = 0.9;

    // Pass 2 — key statistics + company profile. Two extra Yahoo calls per stock and the bulk of a
    // cycle's traffic, which is why it runs on its own slower cadence AND strictly after prices.
    private async Task ImportEnrichment(
        List<KeyValuePair<string, Guid>> crawlOrder,
        CancellationToken cancellationToken
    )
    {
        foreach (var (ticker, commonStockId) in crawlOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await SyncKeyStatistics(ticker, commonStockId, cancellationToken);
                await SyncCompanyProfile(ticker, commonStockId, cancellationToken);
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
        }
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
    private async Task<List<KeyValuePair<string, Guid>>> OrderByCrawlPriority(
        Dictionary<string, Guid> tickerMap,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var priceRepo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        var lastDates = await priceRepo
            .GetAll()
            .GroupBy(p => p.CommonStockId)
            .Select(g => new { StockId = g.Key, LastDate = g.Max(p => p.Date) })
            .ToDictionaryAsync(x => x.StockId, x => x.LastDate, cancellationToken);

        return BuildCrawlOrder(tickerMap, lastDates, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    // Pure so the priority rule is pinnable in tests without a database.
    private static List<KeyValuePair<string, Guid>> BuildCrawlOrder(
        Dictionary<string, Guid> tickerMap,
        IReadOnlyDictionary<Guid, DateOnly> lastDates,
        DateOnly today
    )
    {
        var activeSince = today.AddDays(-ActivelyTradedWindowDays);

        return tickerMap
            .Select(kv => new
            {
                Entry = kv,
                LastDate = lastDates.TryGetValue(kv.Value, out var lastDate)
                    ? lastDate
                    : DateOnly.MinValue,
            })
            // false (0) sorts before true (1), so the actively-traded group leads.
            .OrderBy(x => x.LastDate < activeSince)
            .ThenBy(x => x.LastDate)
            .Select(x => x.Entry)
            .ToList();
    }

    // Re-syncs the full price history of every stock that has an unreconciled split, capped per
    // cycle. Yahoo serves the whole history already split-adjusted, so the fix is to overwrite the
    // stored rows with a fresh pull rather than doing ratio arithmetic — self-healing, since the
    // next split re-marks the stock pending and re-syncs it again (#2879).
    private async Task ReconcilePendingSplits(DateOnly today, CancellationToken cancellationToken)
    {
        PendingSplitSelection selection;
        using (var scope = _scopeFactory.CreateScope())
        {
            var manager =
                scope.ServiceProvider.GetRequiredService<SplitPriceReconciliationManager>();
            selection = await manager.SelectPendingStocks(
                _workerOptions.MaxSplitPriceReconciliationsPerCycle
            );
        }

        if (selection.StockIds.Count == 0)
            return;

        _logger.LogInformation(
            "Re-syncing split-adjusted price history for {Count} stock(s) with pending splits",
            selection.StockIds.Count
        );
        if (selection.Skipped > 0)
            _logger.LogInformation(
                "{Remaining} more stock(s) with pending splits exceed the per-cycle cap "
                    + "and will be reconciled on a later cycle",
                selection.Skipped
            );

        var tickers = await ResolveTickers(selection.StockIds, cancellationToken);
        var floor = _workerOptions.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : new DateOnly(2020, 1, 1);

        foreach (var commonStockId in selection.StockIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!tickers.TryGetValue(commonStockId, out var ticker))
            {
                _logger.LogWarning(
                    "No ticker resolved for stock {StockId} with a pending split; leaving it pending",
                    commonStockId
                );
                continue;
            }

            try
            {
                await ReconcileStock(ticker, commonStockId, floor, today, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to fetch split-adjusted history for {Ticker}; leaving its split(s) pending",
                    ticker
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
                    "Error reconciling split-adjusted history for {Ticker}",
                    ticker
                );
                await _errorReporter.Report(
                    ErrorSource.YahooPriceScraper,
                    $"ReconcilePendingSplits({ticker})",
                    ex
                );
            }
        }
    }

    private async Task<Dictionary<Guid, string>> ResolveTickers(
        IReadOnlyList<Guid> stockIds,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        return await stockRepo
            .GetByIds(stockIds)
            .ToDictionaryAsync(s => s.Id, s => s.Ticker, cancellationToken);
    }

    private async Task ReconcileStock(
        string ticker,
        Guid commonStockId,
        DateOnly floor,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var chartData = await _yahooClient.GetChart(ticker, floor, today);

        // A delisted/unresolved ticker returns no prices. Do NOT wipe the existing series in that
        // case — leave the split pending so a later run or another source can handle it.
        if (chartData.Prices.Count == 0)
        {
            _logger.LogWarning(
                "Yahoo returned no prices for {Ticker}; keeping existing rows and leaving its split(s) pending",
                ticker
            );
            return;
        }

        var replaced = await ReplaceStoredPrices(
            ticker,
            commonStockId,
            floor,
            today,
            chartData.Prices,
            cancellationToken
        );
        if (!replaced)
            return;

        // Refresh the authoritative current share count + market cap by refetch, not arithmetic —
        // this is #2879's shares-outstanding requirement.
        await SyncKeyStatistics(ticker, commonStockId, cancellationToken);

        using var scope = _scopeFactory.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<SplitPriceReconciliationManager>();
        var stamped = await manager.StampApplied(commonStockId, DateTime.UtcNow);
        _logger.LogInformation(
            "Reconciled {Ticker}: replaced stored price history and stamped {Count} split(s) applied",
            ticker,
            stamped
        );
    }

    // Transactionally swaps a stock's stored rows in [floor, today] for the fresh fully-adjusted
    // series. Returns false without touching the stored rows when there is nothing usable to store
    // (all rows overflowed the numeric ceiling, or the parent CommonStock was removed) so a stock
    // is never left with an empty series.
    private async Task<bool> ReplaceStoredPrices(
        string ticker,
        Guid commonStockId,
        DateOnly floor,
        DateOnly today,
        List<HistoricalPrice> prices,
        CancellationToken cancellationToken
    )
    {
        var freshRows = MapFreshRows(commonStockId, prices, ticker, today);
        if (freshRows.Count == 0)
        {
            _logger.LogWarning(
                "No storable prices for {Ticker} after the numeric range guard; keeping existing rows",
                ticker
            );
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        // Same GH-1591 guard as the incremental flush: the parent CommonStock can be removed
        // between selection and now, which would trip the FK on insert.
        var stockExists = await stockRepo
            .GetAll()
            .AnyAsync(s => s.Id == commonStockId, cancellationToken);
        if (!stockExists)
        {
            _logger.LogWarning(
                "Skipping reconcile for CommonStock {Id}: parent row was removed",
                commonStockId
            );
            return false;
        }

        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        await ReplacePriceRows(repo, commonStockId, floor, today, freshRows, cancellationToken);
        return true;
    }

    // The transactional core of the replacement: delete the stock's rows in [floor, today], then
    // bulk-insert the fresh rows in batches, all in one transaction so the stock is never left with
    // a partial series on failure. Takes the repo so it is unit-testable without a live worker.
    private static async Task ReplacePriceRows(
        DailyStockPriceRepository repo,
        Guid commonStockId,
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
            return;

        await using var transaction = await repo.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        try
        {
            var existing = await repo.GetByStocks([commonStockId], floor, today)
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

            await transaction.CommitAsync(cancellationToken);
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
        return prices
            .Where(p => !overflowDates.Contains(p.Date))
            .Where(p => IsSettledDailyBar(p.Date, today))
            .Select(p => new DailyStockPrice
            {
                CommonStockId = commonStockId,
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
        string ticker,
        Guid commonStockId,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var startDate = await ResolveStartDate(commonStockId, today, cancellationToken);
        if (!HasSettledTradingDay(startDate, today))
            return NoFetchNeeded;

        // One chart fetch yields the price bars plus any split and dividend
        // events for the window — capture both off the same response, no extra
        // HTTP.
        var chartData = await _yahooClient.GetChart(ticker, startDate, today);

        var inserted = await PersistPrices(
            ticker,
            commonStockId,
            chartData.Prices,
            today,
            cancellationToken
        );

        await CaptureSplits(commonStockId, chartData.Splits);
        await CaptureDividends(commonStockId, chartData.Dividends);

        return new TickerImportResult(Fetched: true, Inserted: inserted);
    }

    private async Task<int> PersistPrices(
        string ticker,
        Guid commonStockId,
        List<HistoricalPrice> prices,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        if (prices.Count == 0)
            return 0;

        // Load existing dates covering the actual response range to avoid duplicates
        var minDate = prices.Min(p => p.Date);
        var maxDate = prices.Max(p => p.Date);
        var existingDates = await GetExistingDates(
            commonStockId,
            minDate,
            maxDate,
            cancellationToken
        );

        var newPrices = MapFreshRows(commonStockId, prices, ticker, today)
            .Where(p => !existingDates.Contains(p.Date))
            .ToList();

        if (newPrices.Count == 0)
            return 0;

        var inserted = await BatchPersister.Persist(newPrices, InsertBatchSize, FlushPriceBatch);

        _logger.LogDebug("Inserted {Count} prices for {Ticker}", inserted, ticker);
        return inserted;
    }

    // Upserts the split events Yahoo returned for this ticker into StockSplit via
    // the CorporateActions capture manager. Resolved in its own scope (mirrors
    // the other per-write scopes); skipped when there are no splits so the common
    // no-split path costs nothing.
    private async Task CaptureSplits(
        Guid commonStockId,
        IReadOnlyCollection<StockSplitEvent> splits
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
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var stock = await stockRepo.Get(commonStockId);
        if (stock == null)
            return;

        var captureManager = scope.ServiceProvider.GetRequiredService<StockSplitCaptureManager>();
        var count = await captureManager.Capture(stock, captured);
        if (count > 0)
            _logger.LogInformation(
                "Captured {Count} stock split(s) for {StockId}",
                count,
                commonStockId
            );
    }

    // Upserts the dividend events Yahoo returned for this ticker into
    // CashDividend via the CorporateActions capture manager. Mirrors
    // CaptureSplits: its own scope, and skipped when there are no dividends so
    // the common no-dividend path costs nothing.
    private async Task CaptureDividends(
        Guid commonStockId,
        IReadOnlyCollection<CashDividendEvent> dividends
    )
    {
        if (dividends.Count == 0)
            return;

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
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var stock = await stockRepo.Get(commonStockId);
        if (stock == null)
            return;

        var captureManager = scope.ServiceProvider.GetRequiredService<CashDividendCaptureManager>();
        var count = await captureManager.Capture(stock, captured);
        if (count > 0)
            _logger.LogInformation(
                "Captured {Count} cash dividend(s) for {StockId}",
                count,
                commonStockId
            );
    }

    private async Task FlushPriceBatch(List<DailyStockPrice> batch)
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        // Each batch holds rows for a single ticker, so a single existence check
        // is enough. Guards GH-1591: CompanySync can delete the parent CommonStock
        // between TickerMapService.Build and this flush, which would otherwise
        // trip FK_DailyStockPrice_CommonStock_CommonStockId at SaveChanges.
        var commonStockId = batch[0].CommonStockId;
        var stockExists = await stockRepo.GetAll().AnyAsync(s => s.Id == commonStockId);
        if (!stockExists)
        {
            _logger.LogWarning(
                "Skipping {Count} prices for CommonStock {Id}: parent row was removed before flush",
                batch.Count,
                commonStockId
            );
            return;
        }

        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        repo.AddRange(batch);
        await repo.SaveChanges();
    }

    private async Task SyncKeyStatistics(
        string ticker,
        Guid commonStockId,
        CancellationToken cancellationToken
    )
    {
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

        var stock = await stockRepo.Get(commonStockId);

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
        // a former foreign private issuer that lost FPI status files 10-K/10-Q while its cover
        // page keeps counting ordinary shares — AKTX filed 10-Q covers of 91.6B ordinary shares
        // against ~1.1M listed ADSs (80,000 ordinary per ADS). Rescaling onto that base inflates
        // market cap by the full ADS ratio, and the damage is undetectable downstream because the
        // stored pair stays self-consistent (cap ÷ shares == the close). No ingested API exposes
        // the registered security's title to flag these issuers authoritatively, so detect the
        // unit mismatch from the figures themselves: when the EDGAR count and Yahoo's own share
        // base are too far apart to be statements of the same unit, keep Yahoo's self-consistent
        // listed-security figures verbatim, exactly like the FPI path. Also stops a garbage EDGAR
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
                .GetByStock(stock)
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
            return;
        await stockRepo.SaveChanges();

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
    // base to rescale from. The caller passes edgarShares == null for foreign private issuers
    // (20-F/40-F), whose EDGAR count is in ordinary shares — a different unit from the US-listed
    // ADR — so they keep Yahoo's self-consistent ADR market cap rather than being rescaled onto
    // the ordinary base, and likewise whenever the EDGAR count and Yahoo's share base are too far
    // apart to be statements of the same unit (a domestic filer still listing ADSs, a garbage
    // cover-page count — see ShareBasisPlausibility).
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
        string ticker,
        Guid commonStockId,
        CancellationToken cancellationToken
    )
    {
        var profile = await _yahooClient.GetCompanyProfile(ticker);
        if (profile == null || string.IsNullOrWhiteSpace(profile.Industry))
            return;

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var industryRepo = scope.ServiceProvider.GetRequiredService<IndustryRepository>();
        var sectorRepo = scope.ServiceProvider.GetRequiredService<SectorRepository>();

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

        var stock = await stockRepo.Get(commonStockId);
        if (stock.IndustryId == industry.Id)
            return;

        stock.IndustryId = industry.Id;
        await stockRepo.SaveChanges();

        _logger.LogDebug(
            "Updated industry for {Ticker}: {Industry} (sector {Sector})",
            ticker,
            profile.Industry,
            profile.Sector ?? "?"
        );
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
        Guid commonStockId,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var forwardOnly = await GetSyncStartDate(commonStockId, cancellationToken);

        var windowStart = today.AddDays(-GapHealWindowDays);
        // Already reaching back past the window (a never-synced stock, or one mid-backfill) — it is
        // going to re-request those sessions anyway, so there is nothing to widen.
        if (forwardOnly <= windowStart)
            return forwardOnly;

        List<DateOnly> storedDates;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
            storedDates = await repo.GetAll()
                .Where(p =>
                    p.CommonStockId == commonStockId && p.Date >= windowStart && p.Date < today
                )
                .Select(p => p.Date)
                .ToListAsync(cancellationToken);
        }

        var earliestGap = FindEarliestGap(storedDates, windowStart, today);
        return earliestGap is { } gap && gap < forwardOnly ? gap : forwardOnly;
    }

    // The earliest settled trading day in [windowStart, today) with no stored bar, or null when the
    // window is complete. Pure so the rule is pinnable without a database.
    //
    // A stock whose history simply starts inside the window (a new listing) must not read as a hole
    // for every day before its first bar, so the scan begins at the earliest stored date rather than
    // at windowStart. A stock with nothing stored in the window at all has no gap to speak of — it
    // is plain staleness, which the forward-only start date already covers.
    private static DateOnly? FindEarliestGap(
        List<DateOnly> storedDates,
        DateOnly windowStart,
        DateOnly today
    )
    {
        if (storedDates.Count == 0)
            return null;

        var stored = storedDates.ToHashSet();
        var scanFrom = storedDates.Min();
        if (scanFrom < windowStart)
            scanFrom = windowStart;

        for (var date = scanFrom; date < today; date = date.AddDays(1))
        {
            if (UsMarketCalendar.IsTradingDay(date) && !stored.Contains(date))
                return date;
        }

        return null;
    }

    private async Task<DateOnly> GetSyncStartDate(
        Guid commonStockId,
        CancellationToken cancellationToken
    )
    {
        return await SyncStartDate.Resolve<DailyStockPriceRepository>(
            _scopeFactory,
            _workerOptions,
            repo =>
                repo.GetAll()
                    .Where(p => p.CommonStockId == commonStockId)
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

    private static bool HasOverflowPrice(HistoricalPrice p) =>
        Math.Abs(p.Open) > MaxPriceValue
        || Math.Abs(p.High) > MaxPriceValue
        || Math.Abs(p.Low) > MaxPriceValue
        || Math.Abs(p.Close) > MaxPriceValue
        || Math.Abs(p.AdjustedClose) > MaxPriceValue;

    private async Task<HashSet<DateOnly>> GetExistingDates(
        Guid commonStockId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();

        var dates = await repo.GetAll()
            .Where(p =>
                p.CommonStockId == commonStockId && p.Date >= startDate && p.Date <= endDate
            )
            .Select(p => p.Date)
            .ToListAsync(cancellationToken);

        return dates.ToHashSet();
    }
}
