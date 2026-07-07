using System.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
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

    public async Task Import(CancellationToken cancellationToken)
    {
        var tickerMap = await _tickerMapService.Build(
            _workerOptions.TickersToSync,
            cancellationToken
        );
        _logger.LogInformation("Starting Yahoo price sync for {Count} stocks", tickerMap.Count);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Before the forward-only incremental append: reconcile any stock whose stored history is
        // on a pre-split basis. GetSyncStartDate only appends, so a split that landed after the
        // last sync leaves the old rows on the wrong basis (a discontinuity in the series). Re-pull
        // those stocks' full, fully-adjusted history and overwrite the stored rows (#2879).
        await ReconcilePendingSplits(today, cancellationToken);

        var totalInserted = 0;

        // Crawl stalest-first: the ticker map's DB order is stable across cycles, so with a
        // multi-hour crawl any interruption (deploy, crash, rate-limit stall) starves the same
        // tail stocks for days while head stocks re-sync every cycle. Ordering by each stock's
        // last stored price date spends every partial cycle on the most out-of-date stocks;
        // never-synced stocks lead.
        var crawlOrder = await OrderByStalestPrice(tickerMap, cancellationToken);

        foreach (var (ticker, commonStockId) in crawlOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var inserted = await ImportTicker(ticker, commonStockId, today, cancellationToken);
                totalInserted += inserted;
                await SyncKeyStatistics(ticker, commonStockId, cancellationToken);
                await SyncCompanyProfile(ticker, commonStockId, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to fetch data for {Ticker}, skipping", ticker);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown, not a per-ticker fault — rethrow so the worker's cancellation
                // handling sees it instead of recording a phantom error row per deploy.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing data for {Ticker}", ticker);
                await _errorReporter.Report(
                    ErrorSource.YahooPriceScraper,
                    $"ImportTicker({ticker})",
                    ex.Message,
                    ex.StackTrace
                );
            }
        }

        _logger.LogInformation(
            "Yahoo price sync complete. Inserted {Count} new price records",
            totalInserted
        );
    }

    // Orders the ticker map by each stock's most recent stored price date, oldest first (stocks
    // with no prices at all lead). One grouped MAX(Date) query over the price table per cycle.
    private async Task<List<KeyValuePair<string, Guid>>> OrderByStalestPrice(
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

        return tickerMap
            .OrderBy(kv =>
                lastDates.TryGetValue(kv.Value, out var lastDate) ? lastDate : DateOnly.MinValue
            )
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
                    ex.Message,
                    ex.StackTrace
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
    // day's settled bar is appended on the next cycle once the date has rolled over (always after a
    // US market close), so the daily series holds settled closes only.
    private static bool IsSettledDailyBar(DateOnly barDate, DateOnly today) => barDate < today;

    private async Task<int> ImportTicker(
        string ticker,
        Guid commonStockId,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var startDate = await GetSyncStartDate(commonStockId, cancellationToken);
        if (startDate >= today)
            return 0;

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

        return inserted;
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
        var stats = await _yahooClient.GetKeyStatistics(ticker);
        if (stats == null)
            return;
        // Nothing to write if Yahoo returned both fields as zero (missing/unknown).
        if (stats.SharesOutstanding == 0 && stats.MarketCapitalization == 0)
            return;

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
        // shares, a different unit from the US-listed ADR Yahoo prices. Yahoo already returns a
        // correct, self-consistent ADR market cap + ADR share count for the ticker, so reconciling
        // it onto the EDGAR ordinary base would inflate market cap by the ADR ratio (e.g. Latam
        // Airlines $16.7B -> $33T at ~2000x). Drop the EDGAR count for these issuers so Yahoo's ADR
        // figures stand verbatim; the reconciliation stays in force for domestic 10-K/10-Q filers.
        if (
            edgarShares != null
            && await sharesProvider.IsForeignPrivateIssuer(stock, cancellationToken)
        )
            edgarShares = null;

        // Per-field conservative writes: only update on actual change, and never
        // overwrite a known value with 0 (treated as Yahoo "unknown" by the rest of
        // the codebase).
        var changed = false;
        if (
            edgarShares == null
            && stats.SharesOutstanding != 0
            && stock.SharesOutStanding != stats.SharesOutstanding
        )
        {
            stock.SharesOutStanding = stats.SharesOutstanding;
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

    // Yahoo's market cap is its own (per-share-class / stale) share count times price. When EDGAR
    // is the authoritative share source the importer keeps EDGAR's SharesOutStanding, so storing
    // Yahoo's market cap verbatim leaves the two figures on different share bases — they disagree
    // by the share-count ratio (a reverse-split lag inflates market cap ~20x, COPR #3575; a
    // multi-class issuer understates Yahoo's shares ~2x, #2503). Rescale Yahoo's market cap onto
    // the EDGAR base (== EDGAR shares × the same implied price) so market cap stays consistent with
    // SharesOutStanding and the screener's derived price (market cap ÷ shares) holds. Falls back to
    // Yahoo's figure when there is no EDGAR count or no usable Yahoo share base to rescale from.
    // The caller passes edgarShares == null for foreign private issuers (20-F/40-F), whose EDGAR
    // count is in ordinary shares — a different unit from the US-listed ADR — so they keep Yahoo's
    // self-consistent ADR market cap rather than being rescaled onto the ordinary base.
    //
    // Yahoo sometimes returns no market cap at all (summaryDetail.marketCap missing — common for
    // multi-class issuers it hasn't reconciled, #5238): with no Yahoo market cap there is nothing
    // to rescale, and the figure would otherwise stay stale forever even after EDGAR's share count
    // is corrected. When a current price is available (the same import cycle's freshly-fetched
    // close), compute EDGAR shares × price directly instead of leaving the stored value untouched.
    private static double ReconcileMarketCap(
        long? edgarShares,
        long yahooShares,
        double yahooMarketCap,
        decimal? currentPrice = null
    )
    {
        if (edgarShares is > 0 && yahooShares > 0 && yahooMarketCap > 0)
            return yahooMarketCap * ((double)edgarShares.Value / yahooShares);
        if (edgarShares is > 0 && currentPrice is > 0)
            return (double)edgarShares.Value * (double)currentPrice.Value;
        return yahooMarketCap;
    }

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
