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
    // FINRA summaryTypeCode values for the per-symbol weekly aggregates.
    private const string AtsSummaryTypeCode = "ATS_W_SMBL";
    private const string NonAtsOtcSummaryTypeCode = "OTC_W_SMBL";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OffExchangeVolumeImportService> _logger;
    private readonly IFinraClient _finraClient;
    private readonly TickerMapService _tickerMapService;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;

    public OffExchangeVolumeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<OffExchangeVolumeImportService> logger,
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
        // the loop below re-derives the forward edge from the stored span, so the full
        // window is reconsidered every cycle rather than only moving past the latest week.
        var floor = SyncDateResolver.Resolve(default, _workerOptions);

        // FINRA partitions the OTC/ATS Transparency feed by the Monday that starts
        // each reporting week, so iterate Monday-by-Monday rather than day-by-day.
        var startWeek = ToWeekStart(floor);
        var endWeek = ToWeekStart(DateOnly.FromDateTime(DateTime.UtcNow));

        if (startWeek > endWeek)
        {
            _logger.LogInformation(
                "Off-exchange volume sync floor {Week} is in the future; nothing to import",
                startWeek
            );
            return;
        }

        // The weeks already stored span [earliestWeek, latestWeek]; the loop skips that span
        // and imports the weeks outside it, backfilling history below the earliest stored
        // week and importing forward past the latest, without re-fetching finished weeks.
        // The previous implementation only moved forward from the latest stored week, so the
        // pre-collection history could never load.
        DateOnly earliestWeek;
        DateOnly latestWeek;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<OffExchangeVolumeRepository>();
            earliestWeek = await repo.GetEarliestWeek().FirstOrDefaultAsync(cancellationToken);
            latestWeek = await repo.GetLatestWeek().FirstOrDefaultAsync(cancellationToken);
        }

        var hasStored = earliestWeek != default;

        _logger.LogInformation(
            "Importing off-exchange volume from week {Start} to week {End}{Skip}",
            startWeek,
            endWeek,
            hasStored ? $" (skipping already-stored {earliestWeek}..{latestWeek})" : null
        );

        var tickerMap = await _tickerMapService.Build(
            _workerOptions.TickersToSync,
            cancellationToken
        );

        var currentWeek = startWeek;
        while (currentWeek <= endWeek)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip any week already inside the stored span.
            if (hasStored && currentWeek >= earliestWeek && currentWeek <= latestWeek)
            {
                currentWeek = currentWeek.AddDays(7);
                continue;
            }

            await ImportWeek(currentWeek, tickerMap, cancellationToken);
            currentWeek = currentWeek.AddDays(7);
        }
    }

    private async Task ImportWeek(
        DateOnly weekStartDate,
        IReadOnlyDictionary<string, Guid> tickerMap,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var records = await _finraClient.GetWeeklyOffExchangeVolume(weekStartDate);

            if (records.Count == 0)
            {
                _logger.LogDebug(
                    "No off-exchange volume data for week {Week}, skipping",
                    weekStartDate
                );
                return;
            }

            var merged = MergeRecordsByStock(records, tickerMap, weekStartDate);

            await UpsertWeek(merged.Values, weekStartDate, cancellationToken);

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
                "Failed to fetch off-exchange volume for week {Week}, skipping",
                weekStartDate
            );
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

    // Idempotent upsert: update the existing row for each (CommonStockId, WeekStartDate),
    // otherwise insert. Re-runs of the same week overwrite rather than duplicate, so a
    // resumed or rescheduled scrape never produces two rows for the same stock and week.
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
            b => b.CommonStockId,
            cancellationToken
        );
        var dropped = batch.Count - validBatch.Count;
        if (dropped > 0)
        {
            _logger.LogWarning(
                "Dropped {Dropped} off-exchange volume rows for week {Week} referencing CommonStockIds no longer in the database",
                dropped,
                weekStartDate
            );
        }

        if (validBatch.Count == 0)
            return;

        var existing = await repo.GetByWeek(weekStartDate)
            .ToDictionaryAsync(v => v.CommonStockId, cancellationToken);

        foreach (var volume in validBatch)
        {
            if (existing.TryGetValue(volume.CommonStockId, out var current))
            {
                current.AtsVolume = volume.AtsVolume;
                current.AtsTradeCount = volume.AtsTradeCount;
                current.NonAtsOtcVolume = volume.NonAtsOtcVolume;
                current.NonAtsOtcTradeCount = volume.NonAtsOtcTradeCount;
            }
            else
            {
                repo.Add(volume);
            }
        }

        await repo.SaveChanges();
    }

    // Merge FINRA's two per-symbol weekly aggregate rows into one OffExchangeVolume per
    // (CommonStockId, week): the ATS_W_SMBL row supplies Ats* and the OTC_W_SMBL row
    // supplies NonAtsOtc*. A symbol with only one of the two rows keeps the other pair at
    // zero. Symbols absent from the tickerMap (untracked) and blank symbols are skipped.
    private static Dictionary<Guid, OffExchangeVolume> MergeRecordsByStock(
        List<OffExchangeWeeklyRecord> records,
        IReadOnlyDictionary<string, Guid> tickerMap,
        DateOnly weekStartDate
    )
    {
        var merged = new Dictionary<Guid, OffExchangeVolume>();

        foreach (var record in records)
        {
            if (
                string.IsNullOrEmpty(record.Symbol)
                || !tickerMap.TryGetValue(record.Symbol, out var commonStockId)
            )
            {
                continue;
            }

            if (!merged.TryGetValue(commonStockId, out var volume))
            {
                volume = new OffExchangeVolume
                {
                    CommonStockId = commonStockId,
                    WeekStartDate = weekStartDate,
                };
                merged[commonStockId] = volume;
            }

            var shares = record.TotalWeeklyShareQuantity ?? 0;
            var trades = record.TotalWeeklyTradeCount ?? 0;

            if (record.SummaryTypeCode == AtsSummaryTypeCode)
            {
                volume.AtsVolume += shares;
                volume.AtsTradeCount += trades;
            }
            else if (record.SummaryTypeCode == NonAtsOtcSummaryTypeCode)
            {
                volume.NonAtsOtcVolume += shares;
                volume.NonAtsOtcTradeCount += trades;
            }
        }

        return merged;
    }

    // Normalize any date to the Monday that starts its FINRA reporting week.
    private static DateOnly ToWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}
