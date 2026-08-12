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
public class OffExchangeVolumeImportService
{
    // v2: v1 partitions were imported through the case-insensitive ticker map that folded
    // FINRA's lowercase sibling-security symbols onto common tickers. The bump orphans the v1
    // markers so every week still inside FINRA's rolling publication window (~1 year)
    // re-imports and the upsert replaces the corrupted totals; weeks that have aged out of the
    // window cannot be re-fetched and stay as stored.
    // v3: class-share symbol resolution (dot/compressed spellings onto stored dash tickers,
    // #4369) — the bump re-imports every week FINRA still publishes so dual-class weeks fill
    // in; weeks aged out of the ~1-year rolling window are unhealable and stay as stored.
    private const string Dataset = "off-exchange-weekly-v3";
    private const int CorrectionLookbackWeeks = 8;
    private static readonly TimeSpan RecentPartitionRefreshInterval = TimeSpan.FromHours(24);
    private static readonly HashSet<string> CompletePublicationTiers = new(
        ["T1", "T2", "OTCE"],
        StringComparer.OrdinalIgnoreCase
    );

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OffExchangeVolumeImportService> _logger;
    private readonly IFinraClient _finraClient;
    private readonly TickerMapService _tickerMapService;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;
    private readonly FinraImportPartitionTracker _partitionTracker;
    private readonly TimeProvider _timeProvider;

    public OffExchangeVolumeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<OffExchangeVolumeImportService> logger,
        IFinraClient finraClient,
        TickerMapService tickerMapService,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions,
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
        _partitionTracker = partitionTracker;
        _timeProvider = timeProvider;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var configuredFloor = ToWeekStart(SyncDateResolver.Resolve(default, _workerOptions));
        var currentDatasetFloor = ToWeekStart(today.AddYears(-1).AddDays(7));
        var startWeek =
            configuredFloor > currentDatasetFloor ? configuredFloor : currentDatasetFloor;
        var endWeek = ToWeekStart(today);

        if (startWeek > endWeek)
        {
            _logger.LogInformation(
                "Off-exchange volume sync floor {Week} is in the future; nothing to import",
                startWeek
            );
            return;
        }

        var scopeKey = FinraImportScope.Resolve(_workerOptions.TickersToSync);
        var completed = await _partitionTracker.GetCompleted(
            Dataset,
            scopeKey,
            startWeek,
            endWeek,
            cancellationToken
        );
        var weeks = CandidateWeeks(startWeek, endWeek, now.UtcDateTime, completed).ToList();

        _logger.LogInformation(
            "Reconciling {Attempted} FINRA off-exchange weekly partitions in {Start}..{End}; {Completed} already complete for scope {Scope}",
            weeks.Count,
            startWeek,
            endWeek,
            completed.Count,
            scopeKey
        );

        // Ordinal for the same reason as the daily lane: FINRA symbol casing is identity
        // (lowercase suffix = a different security), so a case-insensitive map merges two
        // securities' weekly volumes. The dataset-key bump above re-imports every week FINRA
        // still publishes; only weeks that have aged out of the rolling window stay corrupt.
        var tickerMap = await _tickerMapService.Build(
            _workerOptions.TickersToSync,
            cancellationToken,
            StringComparer.Ordinal
        );
        var compressedIndex = FinraClassShareSymbols.BuildCompressedIndex(
            tickerMap,
            StringComparer.Ordinal
        );
        foreach (var week in weeks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportWeek(
                week,
                tickerMap,
                compressedIndex,
                scopeKey,
                now.UtcDateTime,
                cancellationToken
            );
        }
    }

    private static IEnumerable<DateOnly> CandidateWeeks(
        DateOnly startWeek,
        DateOnly endWeek,
        DateTime now,
        IReadOnlyDictionary<DateOnly, FinraImportPartition> completed
    )
    {
        var refreshCutoff = endWeek.AddDays(-7 * CorrectionLookbackWeeks);
        var refreshBefore = now - RecentPartitionRefreshInterval;

        for (var week = endWeek; week >= startWeek; week = week.AddDays(-7))
        {
            if (!completed.TryGetValue(week, out var partition))
            {
                yield return week;
                continue;
            }

            if (week >= refreshCutoff && partition.ImportedAt <= refreshBefore)
                yield return week;
        }
    }

    private async Task ImportWeek(
        DateOnly weekStartDate,
        IReadOnlyDictionary<string, Guid> tickerMap,
        IReadOnlyDictionary<string, Guid> compressedIndex,
        string scopeKey,
        DateTime importedAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var records = await _finraClient.GetWeeklyOffExchangeVolume(weekStartDate);
            if (records.Count == 0)
            {
                _logger.LogDebug(
                    "No off-exchange volume data for week {Week}, leaving it retryable",
                    weekStartDate
                );
                return;
            }

            var publishedTiers = PublishedTiers(records);
            if (!CompletePublicationTiers.IsSubsetOf(publishedTiers))
            {
                LogMissingPublicationTiers(weekStartDate, publishedTiers);
                return;
            }

            var merged = OffExchangeVolumeMerger.Merge(
                records,
                tickerMap,
                compressedIndex,
                weekStartDate
            );
            await UpsertWeek(merged.Values, weekStartDate, cancellationToken);
            await _partitionTracker.MarkImported(
                Dataset,
                scopeKey,
                weekStartDate,
                importedAt,
                cancellationToken
            );

            _logger.LogInformation(
                "Imported {Count} off-exchange volume records for week {Week}",
                merged.Count,
                weekStartDate
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch off-exchange volume for week {Week}, leaving it retryable",
                weekStartDate
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error importing off-exchange volume for week {Week}",
                weekStartDate
            );
            await _errorReporter.Report(
                ErrorSource.FinraScraper,
                "OffExchangeVolume.ImportWeek",
                ex,
                $"week: {weekStartDate}"
            );
        }
    }

    private static HashSet<string> PublishedTiers(List<OffExchangeWeeklyRecord> records)
    {
        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.TierIdentifier))
            .Select(record => record.TierIdentifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void LogMissingPublicationTiers(
        DateOnly weekStartDate,
        IReadOnlySet<string> publishedTiers
    )
    {
        var missing = CompletePublicationTiers
            .Where(tier => !publishedTiers.Contains(tier))
            .OrderBy(tier => tier, StringComparer.Ordinal)
            .ToList();
        _logger.LogInformation(
            "Off-exchange week {Week} is still awaiting FINRA tiers {MissingTiers}",
            weekStartDate,
            string.Join(", ", missing)
        );
    }

    private async Task UpsertWeek(
        IEnumerable<OffExchangeVolume> volumes,
        DateOnly weekStartDate,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var repo = scope.ServiceProvider.GetRequiredService<OffExchangeVolumeRepository>();

        var batch = volumes.ToList();
        var validBatch = await stockRepo.FilterByExistingStocks(
            batch,
            volume => volume.CommonStockId,
            cancellationToken
        );
        LogDroppedRows(batch.Count - validBatch.Count, weekStartDate);

        var existing = await repo.GetByWeek(weekStartDate)
            .ToDictionaryAsync(volume => volume.CommonStockId, cancellationToken);
        foreach (var volume in validBatch)
            UpsertVolume(repo, existing, volume);

        await repo.SaveChanges();
    }

    private void LogDroppedRows(int dropped, DateOnly weekStartDate)
    {
        if (dropped == 0)
            return;

        _logger.LogWarning(
            "Dropped {Dropped} off-exchange volume rows for week {Week} referencing CommonStockIds no longer in the database",
            dropped,
            weekStartDate
        );
    }

    private static void UpsertVolume(
        OffExchangeVolumeRepository repository,
        IReadOnlyDictionary<Guid, OffExchangeVolume> existing,
        OffExchangeVolume volume
    )
    {
        if (!existing.TryGetValue(volume.CommonStockId, out var current))
        {
            repository.Add(volume);
            return;
        }

        current.AtsVolume = volume.AtsVolume;
        current.AtsTradeCount = volume.AtsTradeCount;
        current.NonAtsOtcVolume = volume.NonAtsOtcVolume;
        current.NonAtsOtcTradeCount = volume.NonAtsOtcTradeCount;
    }

    private static DateOnly ToWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}
