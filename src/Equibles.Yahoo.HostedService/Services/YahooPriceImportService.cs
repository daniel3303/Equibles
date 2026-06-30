using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
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
        var totalInserted = 0;

        foreach (var (ticker, commonStockId) in tickerMap)
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

        var prices = await _yahooClient.GetHistoricalPrices(ticker, startDate, today);
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

        var overflowDates = WarnAndCollectOverflowDates(prices, ticker);

        var newPrices = prices
            .Where(p => !existingDates.Contains(p.Date) && !overflowDates.Contains(p.Date))
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

        if (newPrices.Count == 0)
            return 0;

        var inserted = await BatchPersister.Persist(newPrices, InsertBatchSize, FlushPriceBatch);

        _logger.LogDebug("Inserted {Count} prices for {Ticker}", inserted, ticker);
        return inserted;
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
