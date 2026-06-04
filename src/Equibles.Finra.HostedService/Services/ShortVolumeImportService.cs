using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Finra.HostedService.Services;

[Service]
public class ShortVolumeImportService
{
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShortVolumeImportService> _logger;
    private readonly IFinraClient _finraClient;
    private readonly TickerMapService _tickerMapService;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;

    public ShortVolumeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShortVolumeImportService> logger,
        IFinraClient finraClient,
        TickerMapService tickerMapService,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _finraClient = finraClient;
        _tickerMapService = tickerMapService;
        _errorReporter = errorReporter;
        _workerOptions = workerOptions.Value;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var startDate = await SyncStartDate.Resolve<DailyShortVolumeRepository>(
            _scopeFactory,
            _workerOptions,
            repo => repo.GetLatestDate(),
            cancellationToken
        );

        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        if (startDate > endDate)
        {
            _logger.LogInformation(
                "Short volume data is up to date (latest: {Date})",
                startDate.AddDays(-1)
            );
            return;
        }

        _logger.LogInformation("Importing short volume from {Start} to {End}", startDate, endDate);

        var tickerMap = await _tickerMapService.Build(
            _workerOptions.TickersToSync,
            cancellationToken
        );

        var currentDate = startDate;
        while (currentDate <= endDate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip weekends
            if (currentDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                currentDate = currentDate.AddDays(1);
                continue;
            }

            await ImportSingleDay(currentDate, tickerMap, cancellationToken);
            currentDate = currentDate.AddDays(1);
        }
    }

    private async Task ImportSingleDay(
        DateOnly date,
        IReadOnlyDictionary<string, Guid> tickerMap,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var records = await _finraClient.GetDailyShortVolume(date);

            if (records.Count == 0)
            {
                _logger.LogDebug("No short volume data for {Date}, skipping", date);
                return;
            }

            var aggregated = AggregateVolumesByStock(records, tickerMap, date);

            var totalInserted = await BatchPersister.Persist(
                aggregated.Values,
                InsertBatchSize,
                async batch =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var stockRepo =
                        scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
                    var repo =
                        scope.ServiceProvider.GetRequiredService<DailyShortVolumeRepository>();

                    // tickerMap was built at the start of Import and can go stale if CompanySyncService
                    // hard-deletes/replaces a stock in parallel. Re-validate each batch against the
                    // current CommonStock table so dangling-FK inserts don't poison the whole batch.
                    var batchStockIds = batch.Select(b => b.CommonStockId).Distinct().ToList();
                    var liveStockIds = await stockRepo.GetExistingIds(
                        batchStockIds,
                        cancellationToken
                    );

                    var validBatch = batch
                        .Where(b => liveStockIds.Contains(b.CommonStockId))
                        .ToList();
                    var dropped = batch.Count - validBatch.Count;
                    if (dropped > 0)
                    {
                        _logger.LogWarning(
                            "Dropped {Dropped} short volume rows for {Date} referencing CommonStockIds no longer in the database",
                            dropped,
                            date
                        );
                    }

                    if (validBatch.Count == 0)
                        return;

                    repo.AddRange(validBatch);
                    await repo.SaveChanges();
                }
            );

            _logger.LogInformation(
                "Imported {Count} short volume records for {Date}",
                totalInserted,
                date
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch short volume for {Date}, skipping", date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing short volume for {Date}", date);
            await _errorReporter.Report(
                ErrorSource.FinraScraper,
                "ShortVolume.ImportDate",
                ex.Message,
                ex.StackTrace,
                $"date: {date}"
            );
        }
    }

    // Aggregate volumes across all markets per stock so one DailyShortVolume row per
    // (CommonStockId, currentDate) is persisted instead of one per FINRA market venue.
    private static Dictionary<Guid, DailyShortVolume> AggregateVolumesByStock(
        List<ShortVolumeRecord> records,
        IReadOnlyDictionary<string, Guid> tickerMap,
        DateOnly currentDate
    )
    {
        var aggregated = new Dictionary<Guid, DailyShortVolume>();

        foreach (var record in records)
        {
            if (
                string.IsNullOrEmpty(record.Symbol)
                || !tickerMap.TryGetValue(record.Symbol, out var commonStockId)
            )
            {
                continue;
            }

            if (aggregated.TryGetValue(commonStockId, out var existing))
            {
                existing.ShortVolume += record.ShortVolume ?? 0;
                existing.ShortExemptVolume += record.ShortExemptVolume ?? 0;
                existing.TotalVolume += record.TotalVolume ?? 0;
            }
            else
            {
                aggregated[commonStockId] = new DailyShortVolume
                {
                    CommonStockId = commonStockId,
                    Date = currentDate,
                    ShortVolume = record.ShortVolume ?? 0,
                    ShortExemptVolume = record.ShortExemptVolume ?? 0,
                    TotalVolume = record.TotalVolume ?? 0,
                };
            }
        }

        return aggregated;
    }
}
