using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Daily safety-net for the per-quarter AUM and sector-allocation snapshots
/// that power /holdings/stats and /holdings/trends.
///
/// The hot path is the consumer/drain pair
/// (<see cref="Consumers.Filings13FImportedConsumer"/> marks dirty,
/// <see cref="AumSnapshotDrainWorker"/> rebuilds after cooldown). This
/// worker rebuilds the <see cref="RecentQuartersToRebuild"/> most recent
/// quarters once a day — a belt-and-suspenders pass that
/// reconciles snapshots even if a bus message was lost AND the dirty flag
/// was never set. Older quarters are effectively frozen: 13F amendments
/// after a few quarters are rare and trigger their own consumer event
/// anyway.
///
/// On boot, the worker runs a one-off backfill of every quarter with an
/// extended <c>CommandTimeout</c> whenever the snapshot tables don't yet
/// cover every quarter present in the holdings table. The naive "snapshot
/// tables empty" gate this replaced lost the backfill whenever the
/// consumer beat the worker to inserting the first row.
///
/// Every boot restarts the daily cycle, so a deploy-heavy day would pay the
/// recent-quarter rebuild once per deploy. A cycle is skipped while every
/// recent quarter was rebuilt within <see cref="FreshnessWindow"/>, and the
/// next cycle is due when the oldest of those rebuilds turns
/// <see cref="SleepInterval"/> old, so the daily cadence survives restarts.
/// The dirty flag plays no part in that decision: in filing season every
/// recent quarter is marked again within minutes of each drain, the drain owns
/// dirty quarters through its lease and cooldown, and this rebuild never clears
/// the flag, so treating dirty as stale only repeated the drain's work at boot.
/// </summary>
public class AumSnapshotRebuildWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HoldingsAggregateRefreshService _refreshService;
    private readonly ILogger<AumSnapshotRebuildWorker> _logger;

    // Exposed as virtual seams so tests can collapse the waits without
    // changing production behaviour.
    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(5);
    protected virtual TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected virtual TimeSpan BackfillCommandTimeout => TimeSpan.FromMinutes(30);
    protected virtual int RecentQuartersToRebuild => 4;
    protected virtual TimeSpan FreshnessWindow => TimeSpan.FromHours(20);

    public AumSnapshotRebuildWorker(
        IServiceScopeFactory scopeFactory,
        HoldingsAggregateRefreshService refreshService,
        ILogger<AumSnapshotRebuildWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _refreshService = refreshService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (StartupDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(StartupDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // A throw out of TryBackfillIfNeeded propagates out of ExecuteAsync —
        // BackgroundService treats that as fatal and shuts the worker down
        // for the process lifetime. Catch everything except cancellation so a
        // transient DB hiccup at boot time doesn't kill the safety-net.
        try
        {
            await TryBackfillIfNeeded(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AUM snapshot first-boot backfill failed; daily safety-net will reconcile recent quarters on each cycle"
            );
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = SleepInterval;
            try
            {
                if (await LoadOldestFreshRebuild(stoppingToken) is { } rebuiltAt)
                {
                    delay = NextCycleDelay(rebuiltAt, DateTime.UtcNow, SleepInterval);
                    _logger.LogInformation(
                        "Recent holdings snapshots were rebuilt within {Window}; skipping this safety-net cycle, next in {Delay}",
                        FreshnessWindow,
                        delay
                    );
                }
                else
                {
                    _logger.LogInformation(
                        "Running daily AUM snapshot safety-net rebuild for last {Quarters} quarter(s)",
                        RecentQuartersToRebuild
                    );
                    await _refreshService.RebuildRecentAsync(
                        RecentQuartersToRebuild,
                        stoppingToken
                    );
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "AUM snapshot safety-net rebuild failed; will retry next cycle"
                );
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // The AUM row's ComputedAt stands for the whole quarter rebuild: RebuildQuarter writes
    // every snapshot family in one transaction, and the boot backfill above has already
    // covered missing families.
    private async Task<DateTime?> LoadOldestFreshRebuild(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var recentQuarters = await InstitutionalHoldingReportDateQueries
            .Get13FReportDates(dbContext)
            .OrderByDescending(d => d)
            .Take(RecentQuartersToRebuild)
            .ToListAsync(cancellationToken);
        var snapshots = await dbContext
            .Set<AumQuarterlySnapshot>()
            .Where(s => recentQuarters.Contains(s.ReportDate))
            .ToListAsync(cancellationToken);
        return OldestFreshRebuild(recentQuarters, snapshots, DateTime.UtcNow, FreshnessWindow);
    }

    // The oldest recent rebuild when every recent quarter is fresh; null means the cycle must
    // rebuild. No quarters on file counts as fresh, as the old empty rebuild did. A dirty
    // quarter is still fresh: the drain rebuilds it after its cooldown, and a consumer stub
    // (zero aggregates stamped at its event time) waits for that same drain pass.
    internal static DateTime? OldestFreshRebuild(
        IReadOnlyCollection<DateOnly> recentQuarters,
        IReadOnlyCollection<AumQuarterlySnapshot> snapshots,
        DateTime now,
        TimeSpan freshnessWindow
    )
    {
        var threshold = now - freshnessWindow;
        var rebuilds = new List<DateTime>();
        foreach (var quarter in recentQuarters)
        {
            var fresh = snapshots.FirstOrDefault(s =>
                s.ReportDate == quarter && s.ComputedAt >= threshold
            );
            if (fresh == null)
            {
                return null;
            }
            rebuilds.Add(fresh.ComputedAt);
        }
        return rebuilds.Count == 0 ? now : rebuilds.Min();
    }

    // Wake when the oldest recent rebuild turns a cycle old, never earlier than now.
    internal static TimeSpan NextCycleDelay(
        DateTime rebuiltAt,
        DateTime now,
        TimeSpan sleepInterval
    )
    {
        var due = rebuiltAt + sleepInterval - now;
        return due > TimeSpan.Zero ? due : TimeSpan.Zero;
    }

    private async Task TryBackfillIfNeeded(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        // Coverage is measured against distinct Form-13F quarters only — the
        // same set RebuildAllAsync enumerates. Schedule 13D/G rows carry
        // per-day event dates that inflate the all-types distinct count but
        // can never produce a snapshot row, so a gate comparing against it
        // could never be satisfied and would re-run the full backfill on
        // every boot, forever.
        var form13FQuarters = await InstitutionalHoldingReportDateQueries
            .Get13FReportDates(dbContext)
            .CountAsync(cancellationToken);
        if (form13FQuarters == 0)
        {
            return;
        }

        var snapshotQuarters = await dbContext
            .Set<AumQuarterlySnapshot>()
            .CountAsync(cancellationToken);
        // RebuildQuarter also materialises stock, exact-listing and holder activity, so a
        // quarter isn't fully covered until every snapshot family exists for it. Otherwise a
        // newly-added snapshot
        // table would never backfill once AUM is complete.
        var activityQuarters = await dbContext
            .Set<StockQuarterlyActivity>()
            .Select(s => s.ReportDate)
            .Distinct()
            .CountAsync(cancellationToken);
        var holderQuarters = await dbContext
            .Set<HolderQuarterlySnapshot>()
            .Select(s => s.ReportDate)
            .Distinct()
            .CountAsync(cancellationToken);
        var listingQuarters = await dbContext
            .Set<StockQuarterlyListingActivity>()
            .Where(s => !s.IsCombined)
            .Select(s => s.ReportDate)
            .Distinct()
            .CountAsync(cancellationToken);
        if (
            snapshotQuarters >= form13FQuarters
            && activityQuarters >= form13FQuarters
            && holderQuarters >= form13FQuarters
            && listingQuarters >= form13FQuarters
        )
        {
            return;
        }

        _logger.LogInformation(
            "Holdings snapshot coverage incomplete (AUM {Snapshots}, activity {Activity}, listing {ListingQuarters}, holder {HolderQuarters} of {Form13FQuarters} 13F quarters) — running backfill with {Timeout}s command timeout",
            snapshotQuarters,
            activityQuarters,
            listingQuarters,
            holderQuarters,
            form13FQuarters,
            BackfillCommandTimeout.TotalSeconds
        );

        await _refreshService.RebuildAllAsync(BackfillCommandTimeout, cancellationToken);
    }
}
