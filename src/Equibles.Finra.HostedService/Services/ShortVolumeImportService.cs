using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.ShortData.Data.Models;
using Equibles.ShortData.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.ShortData.HostedService.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.ShortData.HostedService.Services;

[Service]
public class ShortVolumeImportService {
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShortVolumeImportService> _logger;
    private readonly IFinraClient _finraClient;
    private readonly TickerMapService _tickerMapService;
    private readonly FinraScraperOptions _options;
    private readonly WorkerOptions _workerOptions;

    public ShortVolumeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShortVolumeImportService> logger,
        IFinraClient finraClient,
        TickerMapService tickerMapService,
        IOptions<FinraScraperOptions> options,
        IOptions<WorkerOptions> workerOptions
    ) {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _finraClient = finraClient;
        _tickerMapService = tickerMapService;
        _options = options.Value;
        _workerOptions = workerOptions.Value;
    }

    public async Task Import(CancellationToken cancellationToken) {
        // Determine start date
        DateOnly startDate;
        using (var scope = _scopeFactory.CreateScope()) {
            var repo = scope.ServiceProvider.GetRequiredService<DailyShortVolumeRepository>();
            var latestDate = await repo.GetLatestDate().FirstOrDefaultAsync(cancellationToken);

            if (latestDate != default) {
                startDate = latestDate.AddDays(1);
            } else {
                var minDate = _workerOptions.MinSyncDate ?? new DateTime(2020, 1, 1);
                startDate = DateOnly.FromDateTime(minDate);
            }
        }

        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        if (startDate > endDate) {
            _logger.LogInformation("Short volume data is up to date (latest: {Date})", startDate.AddDays(-1));
            return;
        }

        _logger.LogInformation("Importing short volume from {Start} to {End}", startDate, endDate);

        var tickerMap = await _tickerMapService.Build(_options.TickersToSync, cancellationToken);

        var currentDate = startDate;
        while (currentDate <= endDate) {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip weekends
            if (currentDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) {
                currentDate = currentDate.AddDays(1);
                continue;
            }

            try {
                var records = await _finraClient.GetDailyShortVolume(currentDate);

                if (records.Count == 0) {
                    _logger.LogDebug("No short volume data for {Date}, skipping", currentDate);
                    currentDate = currentDate.AddDays(1);
                    continue;
                }

                // Aggregate volumes across all markets per stock
                var aggregated = new Dictionary<Guid, DailyShortVolume>();

                foreach (var record in records) {
                    if (string.IsNullOrEmpty(record.Symbol)
                        || !tickerMap.TryGetValue(record.Symbol, out var commonStockId)) {
                        continue;
                    }

                    if (aggregated.TryGetValue(commonStockId, out var existing)) {
                        existing.ShortVolume += record.ShortVolume ?? 0;
                        existing.ShortExemptVolume += record.ShortExemptVolume ?? 0;
                        existing.TotalVolume += record.TotalVolume ?? 0;
                    } else {
                        aggregated[commonStockId] = new DailyShortVolume {
                            CommonStockId = commonStockId,
                            Date = currentDate,
                            ShortVolume = record.ShortVolume ?? 0,
                            ShortExemptVolume = record.ShortExemptVolume ?? 0,
                            TotalVolume = record.TotalVolume ?? 0,
                        };
                    }
                }

                var batch = new List<DailyShortVolume>(InsertBatchSize);
                var totalInserted = 0;

                foreach (var item in aggregated.Values) {
                    batch.Add(item);

                    if (batch.Count >= InsertBatchSize) {
                        await FlushBatch(batch);
                        totalInserted += batch.Count;
                        batch.Clear();
                    }
                }

                if (batch.Count > 0) {
                    await FlushBatch(batch);
                    totalInserted += batch.Count;
                    batch.Clear();
                }

                _logger.LogInformation("Imported {Count} short volume records for {Date}", totalInserted, currentDate);
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to fetch short volume for {Date}, skipping", currentDate);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error importing short volume for {Date}", currentDate);
                await ReportError("ShortVolume.ImportDate", ex.Message, ex.StackTrace, $"date: {currentDate}");
            }

            currentDate = currentDate.AddDays(1);
        }
    }

    private async Task FlushBatch(List<DailyShortVolume> items) {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyShortVolumeRepository>();
        repo.AddRange(items);
        await repo.SaveChanges();
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.ShortDataScraper, context, message, stackTrace, requestSummary);
        } catch { }
    }
}
