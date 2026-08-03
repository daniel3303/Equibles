using System.Net;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Core.Calendars;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Configuration;
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
    private const string Dataset = "daily-short-volume-files-v1";
    private const int CorrectionLookbackDays = 7;
    private static readonly DateOnly FirstConsolidatedFileDate = new(2018, 8, 1);
    private static readonly TimeSpan RecentPartitionRefreshInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShortVolumeImportService> _logger;
    private readonly IFinraClient _finraClient;
    private readonly TickerMapService _tickerMapService;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;
    private readonly FinraScraperOptions _finraOptions;
    private readonly FinraImportPartitionTracker _partitionTracker;
    private readonly TimeProvider _timeProvider;

    public ShortVolumeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShortVolumeImportService> logger,
        IFinraClient finraClient,
        TickerMapService tickerMapService,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions,
        IOptions<FinraScraperOptions> finraOptions,
        FinraImportPartitionTracker partitionTracker,
        TimeProvider timeProvider
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _finraClient = finraClient;
        _tickerMapService = tickerMapService;
        _errorReporter = errorReporter;
        _workerOptions = workerOptions.Value;
        _finraOptions = finraOptions.Value;
        _partitionTracker = partitionTracker;
        _timeProvider = timeProvider;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var floor = SyncDateResolver.Resolve(default, _workerOptions);
        if (floor < FirstConsolidatedFileDate)
            floor = FirstConsolidatedFileDate;

        var now = _timeProvider.GetUtcNow();
        var endDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (floor > endDate)
        {
            _logger.LogInformation(
                "Short volume sync floor {Floor} is in the future; nothing to import",
                floor
            );
            return;
        }

        var scopeKey = FinraImportScope.Resolve(_workerOptions.TickersToSync);
        var completed = await _partitionTracker.GetCompleted(
            Dataset,
            scopeKey,
            floor,
            endDate,
            cancellationToken
        );
        var dates = CandidateDates(floor, endDate, now.UtcDateTime, completed)
            .Take(Math.Max(1, _finraOptions.ShortVolumeBackfillDatesPerCycle))
            .ToList();

        _logger.LogInformation(
            "Reconciling {Attempted} FINRA daily short-volume partitions in {Start}..{End}; {Completed} already complete for scope {Scope}",
            dates.Count,
            floor,
            endDate,
            completed.Count,
            scopeKey
        );

        var tickerMap = await _tickerMapService.Build(
            _workerOptions.TickersToSync,
            cancellationToken
        );
        foreach (var date in dates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportSingleDay(date, tickerMap, scopeKey, now.UtcDateTime, cancellationToken);
        }
    }

    private static IEnumerable<DateOnly> CandidateDates(
        DateOnly floor,
        DateOnly endDate,
        DateTime now,
        IReadOnlyDictionary<DateOnly, FinraImportPartition> completed
    )
    {
        var refreshCutoff = endDate.AddDays(-CorrectionLookbackDays);
        var refreshBefore = now - RecentPartitionRefreshInterval;

        for (var date = endDate; date >= floor; date = date.AddDays(-1))
        {
            if (!UsMarketCalendar.IsTradingDay(date))
                continue;

            if (!completed.TryGetValue(date, out var partition))
            {
                yield return date;
                continue;
            }

            if (date >= refreshCutoff && partition.ImportedAt <= refreshBefore)
                yield return date;
        }
    }

    private async Task<bool> ImportSingleDay(
        DateOnly date,
        IReadOnlyDictionary<string, Guid> tickerMap,
        string scopeKey,
        DateTime importedAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var records = await _finraClient.GetDailyShortVolume(date, cancellationToken);
            var aggregated = AggregateVolumesByStock(records, tickerMap, date);
            var totalImported = await UpsertDay(aggregated.Values, date, cancellationToken);
            await _partitionTracker.MarkImported(
                Dataset,
                scopeKey,
                date,
                importedAt,
                cancellationToken
            );

            _logger.LogInformation(
                "Imported {Count} short volume records for {Date}",
                totalImported,
                date
            );
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug(
                "FINRA short-volume files for {Date} are not published yet; leaving the partition retryable",
                date
            );
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch short volume for {Date}, skipping", date);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
            return false;
        }
    }

    private async Task<int> UpsertDay(
        IEnumerable<DailyShortVolume> volumes,
        DateOnly date,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var repo = scope.ServiceProvider.GetRequiredService<DailyShortVolumeRepository>();

        var batch = volumes.ToList();
        var validBatch = await stockRepo.FilterByExistingStocks(
            batch,
            volume => volume.CommonStockId,
            cancellationToken
        );
        LogDroppedRows(batch.Count - validBatch.Count, date);

        var existing = await repo.GetByDate(date)
            .ToDictionaryAsync(volume => volume.CommonStockId, cancellationToken);
        foreach (var volume in validBatch)
            UpsertVolume(repo, existing, volume);

        await repo.SaveChanges();
        return validBatch.Count;
    }

    private void LogDroppedRows(int dropped, DateOnly date)
    {
        if (dropped == 0)
            return;

        _logger.LogWarning(
            "Dropped {Dropped} short volume rows for {Date} referencing CommonStockIds no longer in the database",
            dropped,
            date
        );
    }

    private static void UpsertVolume(
        DailyShortVolumeRepository repository,
        IReadOnlyDictionary<Guid, DailyShortVolume> existing,
        DailyShortVolume volume
    )
    {
        if (!existing.TryGetValue(volume.CommonStockId, out var current))
        {
            repository.Add(volume);
            return;
        }

        current.ShortVolume = volume.ShortVolume;
        current.ShortExemptVolume = volume.ShortExemptVolume;
        current.TotalVolume = volume.TotalVolume;
        current.Market = volume.Market;
    }

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

            if (!aggregated.TryGetValue(commonStockId, out var volume))
            {
                volume = new DailyShortVolume { CommonStockId = commonStockId, Date = currentDate };
                aggregated[commonStockId] = volume;
            }

            volume.ShortVolume += record.ShortVolume ?? 0;
            volume.ShortExemptVolume += record.ShortExemptVolume ?? 0;
            volume.TotalVolume += record.TotalVolume ?? 0;
            volume.Market = MergeMarketCodes(volume.Market, record.MarketCode);
        }

        return aggregated;
    }

    private static string MergeMarketCodes(string current, string additional)
    {
        var markets = new[] { current, additional }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal);
        var merged = string.Join(',', markets);
        return merged.Length == 0 ? null : merged;
    }
}
