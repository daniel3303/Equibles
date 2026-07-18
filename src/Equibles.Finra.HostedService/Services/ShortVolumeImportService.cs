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
        // Resolve the backfill floor (Worker:MinSyncDate or 2020-01-01) by passing `default`:
        // we want the floor itself, not "latest stored + 1". The loop below re-derives the
        // forward edge from the stored span, so the full window is reconsidered every cycle.
        var floor = SyncDateResolver.Resolve(default, _workerOptions);
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        if (floor > endDate)
        {
            _logger.LogInformation(
                "Short volume sync floor {Floor} is in the future; nothing to import",
                floor
            );
            return;
        }

        // The span already stored is [earliest, latest]; the loop skips it and imports the
        // days outside it. That fills the history below the earliest row (a fresh deployment
        // starts with only recent FINRA data) AND keeps importing forward past the latest
        // row, without ever re-fetching a finished day. The previous implementation only
        // moved forward from the latest row, so the pre-collection history could never load.
        DateOnly earliest;
        DateOnly latest;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<DailyShortVolumeRepository>();
            earliest = await repo.GetEarliestDate().FirstOrDefaultAsync(cancellationToken);
            latest = await repo.GetLatestDate().FirstOrDefaultAsync(cancellationToken);
        }

        var hasStored = earliest != default;

        _logger.LogInformation(
            "Importing short volume from {Start} to {End}{Skip}",
            floor,
            endDate,
            hasStored ? $" (skipping already-stored {earliest}..{latest})" : null
        );

        var tickerMap = await _tickerMapService.Build(
            _workerOptions.TickersToSync,
            cancellationToken
        );

        var currentDate = floor;
        while (currentDate <= endDate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip weekends and any day already inside the stored span.
            if (
                currentDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                || (hasStored && currentDate >= earliest && currentDate <= latest)
            )
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

                    var validBatch = await stockRepo.FilterByExistingStocks(
                        batch,
                        b => b.CommonStockId,
                        cancellationToken
                    );
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
                ex,
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
