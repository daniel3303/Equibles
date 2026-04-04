using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
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
        // Above this, bulk-fetch all symbols (cheaper than a huge domainFilters payload with unknown API limits)
        const int filteredFetchThreshold = 500;

        var tickerMap = await _tickerMapService.Build(_workerOptions.TickersToSync, cancellationToken);
        if (tickerMap.Count == 0) {
            _logger.LogInformation("No stocks to track for short interest import");
            return;
        }

        var trackedStockIds = tickerMap.Values.ToHashSet();
        var reverseMap = tickerMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        // Get known settlement dates from DB
        HashSet<DateOnly> knownDates;
        using (var scope = _scopeFactory.CreateScope()) {
            var repo = scope.ServiceProvider.GetRequiredService<ShortInterestRepository>();
            var dates = await repo.GetAllSettlementDates().ToListAsync(cancellationToken);
            knownDates = dates.ToHashSet();
        }

        // Discover new settlement dates from FINRA (bounded scan)
        var maxKnownDate = knownDates.Count > 0 ? knownDates.Max() : default;
        var minDate = SyncDateResolver.Resolve(default, _workerOptions);

        List<DateOnly> newDates;
        try {
            newDates = maxKnownDate != default
                ? await _finraClient.GetShortInterestSettlementDatesAfter(maxKnownDate)
                : await _finraClient.GetShortInterestSettlementDates();
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to discover settlement dates from FINRA");
            await _errorReporter.Report(ErrorSource.FinraScraper, "ShortInterest.DiscoverDates", ex.Message, ex.StackTrace);
            return;
        }

        // Merge known + new dates
        var allDates = new HashSet<DateOnly>(knownDates);
        allDates.UnionWith(newDates);

        var datesToProcess = allDates
            .Where(d => d >= minDate)
            .OrderBy(d => d)
            .ToList();

        if (datesToProcess.Count == 0) {
            _logger.LogInformation("No settlement dates to process");
            return;
        }

        _logger.LogInformation(
            "Processing {Total} settlement dates ({New} new, checking {Known} existing for gaps)",
            datesToProcess.Count, newDates.Count, datesToProcess.Count - newDates.Count);

        var totalImported = 0;
        var datesSkipped = 0;

        foreach (var date in datesToProcess) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                // Determine which tracked stocks are missing data for this date
                HashSet<Guid> existingStockIds;
                using (var scope = _scopeFactory.CreateScope()) {
                    var repo = scope.ServiceProvider.GetRequiredService<ShortInterestRepository>();
                    var ids = await repo.GetStockIdsBySettlementDate(date).ToListAsync(cancellationToken);
                    existingStockIds = ids.ToHashSet();
                }

                var missingStockIds = trackedStockIds.Except(existingStockIds).ToHashSet();

                if (missingStockIds.Count == 0) {
                    datesSkipped++;
                    continue;
                }

                // Fetch from FINRA: bulk if all/many missing, filtered if few missing
                List<ShortInterestRecord> records;
                var useBulkFetch = missingStockIds.Count == trackedStockIds.Count
                                   || missingStockIds.Count > filteredFetchThreshold;

                if (useBulkFetch) {
                    records = await _finraClient.GetShortInterest(date);
                } else {
                    var missingSymbols = missingStockIds
                        .Where(id => reverseMap.ContainsKey(id))
                        .Select(id => reverseMap[id])
                        .ToList();
                    records = await _finraClient.GetShortInterest(date, missingSymbols);
                }

                if (records.Count == 0) {
                    _logger.LogDebug("No short interest data from FINRA for {Date}", date);
                    continue;
                }

                var items = records
                    .Where(r => !string.IsNullOrEmpty(r.Symbol)
                                && tickerMap.TryGetValue(r.Symbol, out var stockId)
                                && missingStockIds.Contains(stockId))
                    .Select(r => new ShortInterest {
                        CommonStockId = tickerMap[r.Symbol],
                        SettlementDate = date,
                        CurrentShortPosition = r.CurrentShortPosition ?? 0,
                        PreviousShortPosition = r.PreviousShortPosition ?? 0,
                        ChangeInShortPosition = r.ChangeInShortPosition ?? 0,
                        AverageDailyVolume = r.AverageDailyVolume,
                        DaysToCover = r.DaysToCover,
                    });

                var inserted = await BatchPersister.Persist(items, InsertBatchSize, async batch => {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<ShortInterestRepository>();
                    repo.AddRange(batch);
                    await repo.SaveChanges();
                });

                totalImported += inserted;
                _logger.LogInformation(
                    "Imported {Count} short interest records for {Date} ({Missing} stocks were missing)",
                    inserted, date, missingStockIds.Count);
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to fetch short interest for {Date}, skipping", date);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error importing short interest for {Date}", date);
                await _errorReporter.Report(ErrorSource.FinraScraper, "ShortInterest.ImportDate", ex.Message, ex.StackTrace, $"date: {date}");
            }
        }

        _logger.LogInformation(
            "Short interest import complete: {Imported} records imported, {Skipped} dates skipped (already complete)",
            totalImported, datesSkipped);
    }

}
