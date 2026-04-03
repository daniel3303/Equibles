using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Worker;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Yahoo.HostedService.Services;

[Service]
public class YahooPriceImportService {
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
    ) {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _yahooClient = yahooClient;
        _tickerMapService = tickerMapService;
        _errorReporter = errorReporter;
        _workerOptions = workerOptions.Value;
    }

    public async Task Import(CancellationToken cancellationToken) {
        var tickerMap = await _tickerMapService.Build(_workerOptions.TickersToSync, cancellationToken);
        _logger.LogInformation("Starting Yahoo price sync for {Count} stocks", tickerMap.Count);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var totalInserted = 0;

        foreach (var (ticker, commonStockId) in tickerMap) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                var inserted = await ImportTicker(ticker, commonStockId, today, cancellationToken);
                totalInserted += inserted;
                await SyncKeyStatistics(ticker, commonStockId, cancellationToken);
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to fetch data for {Ticker}, skipping", ticker);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error importing data for {Ticker}", ticker);
                await _errorReporter.Report(
                    ErrorSource.YahooPriceScraper, $"ImportTicker({ticker})", ex.Message, ex.StackTrace);
            }
        }

        _logger.LogInformation("Yahoo price sync complete. Inserted {Count} new price records", totalInserted);
    }

    private async Task<int> ImportTicker(
        string ticker,
        Guid commonStockId,
        DateOnly today,
        CancellationToken cancellationToken
    ) {
        var startDate = await GetSyncStartDate(commonStockId, cancellationToken);
        if (startDate >= today) return 0;

        var prices = await _yahooClient.GetHistoricalPrices(ticker, startDate, today);
        if (prices.Count == 0) return 0;

        // Load existing dates covering the actual response range to avoid duplicates
        var minDate = prices.Min(p => p.Date);
        var maxDate = prices.Max(p => p.Date);
        var existingDates = await GetExistingDates(commonStockId, minDate, maxDate, cancellationToken);

        var outOfRange = prices.Where(HasOverflowPrice).ToList();
        if (outOfRange.Count > 0) {
            var sample = outOfRange[0];
            _logger.LogWarning(
                "Skipping {Count} prices for {Ticker} exceeding numeric(18,4) limit. " +
                "Sample: {Date} O={Open} H={High} L={Low} C={Close} AC={AdjClose}",
                outOfRange.Count, ticker,
                sample.Date, sample.Open, sample.High, sample.Low, sample.Close, sample.AdjustedClose);
        }

        var overflowDates = outOfRange.Select(p => p.Date).ToHashSet();

        var newPrices = prices
            .Where(p => !existingDates.Contains(p.Date) && !overflowDates.Contains(p.Date))
            .Select(p => new DailyStockPrice {
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

        if (newPrices.Count == 0) return 0;

        var inserted = await BatchPersister.Persist(newPrices, InsertBatchSize, async batch => {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
            repo.AddRange(batch);
            await repo.SaveChanges();
        });

        _logger.LogDebug("Inserted {Count} prices for {Ticker}", inserted, ticker);
        return inserted;
    }

    private async Task SyncKeyStatistics(string ticker, Guid commonStockId, CancellationToken cancellationToken) {
        var stats = await _yahooClient.GetKeyStatistics(ticker);
        if (stats == null || stats.SharesOutstanding == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stock = await stockRepo.Get(commonStockId);
        if (stock.SharesOutStanding == stats.SharesOutstanding) return;

        stock.SharesOutStanding = stats.SharesOutstanding;
        await stockRepo.SaveChanges();

        _logger.LogDebug("Updated shares outstanding for {Ticker}: {Shares}", ticker, stats.SharesOutstanding);
    }

    private async Task<DateOnly> GetSyncStartDate(Guid commonStockId, CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();

        var latestDate = await repo.GetAll()
            .Where(p => p.CommonStockId == commonStockId)
            .Select(p => p.Date)
            .OrderByDescending(d => d)
            .FirstOrDefaultAsync(cancellationToken);

        return SyncDateResolver.Resolve(latestDate, _workerOptions);
    }

    private static bool HasOverflowPrice(HistoricalPrice p) =>
        Math.Abs(p.Open) > MaxPriceValue ||
        Math.Abs(p.High) > MaxPriceValue ||
        Math.Abs(p.Low) > MaxPriceValue ||
        Math.Abs(p.Close) > MaxPriceValue ||
        Math.Abs(p.AdjustedClose) > MaxPriceValue;

    private async Task<HashSet<DateOnly>> GetExistingDates(
        Guid commonStockId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken
    ) {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();

        var dates = await repo.GetAll()
            .Where(p => p.CommonStockId == commonStockId && p.Date >= startDate && p.Date <= endDate)
            .Select(p => p.Date)
            .ToListAsync(cancellationToken);

        return dates.ToHashSet();
    }

}
