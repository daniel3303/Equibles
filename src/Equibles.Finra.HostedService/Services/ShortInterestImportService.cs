using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Finra.HostedService.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Finra.HostedService.Services;

[Service]
public class ShortInterestImportService {
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShortInterestImportService> _logger;
    private readonly IFinraClient _finraClient;
    private readonly TickerMapService _tickerMapService;
    private readonly FinraScraperOptions _options;
    private readonly WorkerOptions _workerOptions;

    public ShortInterestImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShortInterestImportService> logger,
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
        // Determine latest settlement date in DB
        DateOnly latestDate;
        using (var scope = _scopeFactory.CreateScope()) {
            var repo = scope.ServiceProvider.GetRequiredService<ShortInterestRepository>();
            latestDate = await repo.GetLatestSettlementDate().FirstOrDefaultAsync(cancellationToken);
        }

        var minDate = _workerOptions.MinSyncDate != null
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : new DateOnly(2020, 1, 1);

        // Get available settlement dates from FINRA
        List<DateOnly> settlementDates;
        try {
            settlementDates = await _finraClient.GetShortInterestSettlementDates();
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to fetch short interest settlement dates");
            await ReportError("ShortInterest.FetchDates", ex.Message, ex.StackTrace);
            return;
        }

        // Filter to dates after our latest
        var datesToImport = settlementDates
            .Where(d => d > latestDate && d >= minDate)
            .OrderBy(d => d)
            .ToList();

        if (datesToImport.Count == 0) {
            _logger.LogInformation("Short interest data is up to date (latest: {Date})", latestDate);
            return;
        }

        _logger.LogInformation("Importing short interest for {Count} settlement dates", datesToImport.Count);

        var tickerMap = await _tickerMapService.Build(_options.TickersToSync, cancellationToken);

        foreach (var settlementDate in datesToImport) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                var records = await _finraClient.GetShortInterest(settlementDate);

                if (records.Count == 0) {
                    _logger.LogDebug("No short interest data for {Date}", settlementDate);
                    continue;
                }

                var batch = new List<ShortInterest>(InsertBatchSize);
                var totalInserted = 0;

                foreach (var record in records) {
                    if (string.IsNullOrEmpty(record.Symbol)
                        || !tickerMap.TryGetValue(record.Symbol, out var commonStockId)) {
                        continue;
                    }

                    batch.Add(new ShortInterest {
                        CommonStockId = commonStockId,
                        SettlementDate = settlementDate,
                        CurrentShortPosition = record.CurrentShortPosition ?? 0,
                        PreviousShortPosition = record.PreviousShortPosition ?? 0,
                        ChangeInShortPosition = record.ChangeInShortPosition ?? 0,
                        AverageDailyVolume = record.AverageDailyVolume,
                        DaysToCover = record.DaysToCover,
                    });

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

                _logger.LogInformation(
                    "Imported {Count} short interest records for settlement date {Date}",
                    totalInserted, settlementDate);
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to fetch short interest for {Date}, skipping", settlementDate);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error importing short interest for {Date}", settlementDate);
                await ReportError("ShortInterest.ImportDate", ex.Message, ex.StackTrace, $"date: {settlementDate}");
            }
        }
    }

    private async Task FlushBatch(List<ShortInterest> items) {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ShortInterestRepository>();
        repo.AddRange(items);
        await repo.SaveChanges();
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.FinraScraper, context, message, stackTrace, requestSummary);
        } catch { }
    }
}
