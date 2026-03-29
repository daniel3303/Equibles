using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Yahoo.Contracts;
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
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to fetch prices for {Ticker}, skipping", ticker);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error importing prices for {Ticker}", ticker);
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

        var newPrices = prices
            .Where(p => !existingDates.Contains(p.Date))
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
