using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Worker;
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
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;

    public ShortInterestImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShortInterestImportService> logger,
        IFinraClient finraClient,
        TickerMapService tickerMapService,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions
    ) {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _finraClient = finraClient;
        _tickerMapService = tickerMapService;
        _errorReporter = errorReporter;
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
            await _errorReporter.Report(ErrorSource.FinraScraper, "ShortInterest.FetchDates", ex.Message, ex.StackTrace);
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

        var tickerMap = await _tickerMapService.Build(_workerOptions.TickersToSync, cancellationToken);

        foreach (var settlementDate in datesToImport) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                var records = await _finraClient.GetShortInterest(settlementDate);

                if (records.Count == 0) {
                    _logger.LogDebug("No short interest data for {Date}", settlementDate);
                    continue;
                }

                var items = records
                    .Where(r => !string.IsNullOrEmpty(r.Symbol) && tickerMap.ContainsKey(r.Symbol))
                    .Select(r => new ShortInterest {
                        CommonStockId = tickerMap[r.Symbol],
                        SettlementDate = settlementDate,
                        CurrentShortPosition = r.CurrentShortPosition ?? 0,
                        PreviousShortPosition = r.PreviousShortPosition ?? 0,
                        ChangeInShortPosition = r.ChangeInShortPosition ?? 0,
                        AverageDailyVolume = r.AverageDailyVolume,
                        DaysToCover = r.DaysToCover,
                    });

                var totalInserted = await BatchPersister.Persist(items, InsertBatchSize, async batch => {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<ShortInterestRepository>();
                    repo.AddRange(batch);
                    await repo.SaveChanges();
                });

                _logger.LogInformation(
                    "Imported {Count} short interest records for settlement date {Date}",
                    totalInserted, settlementDate);
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to fetch short interest for {Date}, skipping", settlementDate);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error importing short interest for {Date}", settlementDate);
                await _errorReporter.Report(ErrorSource.FinraScraper, "ShortInterest.ImportDate", ex.Message, ex.StackTrace, $"date: {settlementDate}");
            }
        }
    }

}
