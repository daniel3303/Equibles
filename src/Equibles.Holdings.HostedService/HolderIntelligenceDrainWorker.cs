using Equibles.Core.Extensions;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Rebuilds every computable holder-intelligence quarter whose stamp is past the cooldown, oldest
/// first, including quarters computed by an older calculation version. A stamp is cleared only
/// while it still holds the value read before the rebuild, so a stamp written meanwhile survives.
/// Waits while a 13F bulk replay is pending, for at most <see cref="ReplayHold"/>. Quarters before
/// the latest closed one drain in forward passes (<see cref="HistoricalPassStart"/>), so a trickle
/// of late filings for old quarters cannot keep the whole history rebuilding; the latest closed
/// quarter reads their carried state as of the last pass.
/// </summary>
public class HolderIntelligenceDrainWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HolderIntelligenceDrainWorker> _logger;

    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(2);
    protected virtual TimeSpan TickInterval => TimeSpan.FromMinutes(5);
    protected virtual TimeSpan Cooldown => HolderIntelligenceRefreshService.Cooldown;

    // A bulk replay rewrites one archive after another for hours or days, and each archive stamps
    // its quarters and, through the cost slots, every later one. Waiting rebuilds each quarter once
    // after the pass instead of once per archive; past the hold a stalled replay stops keeping the
    // metrics stale.
    protected virtual TimeSpan ReplayHold => TimeSpan.FromDays(4);

    // A changed old quarter re-dirties every later one, so each late filing for it costs a rebuild
    // of the whole history. Old quarters therefore rest between passes: a pass may run for the
    // window, and the next starts once its rebuilds are older than the interval. The window
    // outlasts a full pass, so it only bounds one that stalls.
    protected virtual TimeSpan HistoricalPassWindow => TimeSpan.FromHours(12);
    protected virtual TimeSpan HistoricalPassInterval => TimeSpan.FromHours(24);

    private DateTime? _heldForReplayQueuedAt;
    private DateTime? _restLoggedUntil;

    public HolderIntelligenceDrainWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<HolderIntelligenceDrainWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DrainOnce(DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Holder intelligence drain tick failed; retrying");
                }
                await Task.Delay(TickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
    }

    internal async Task DrainOnce(DateOnly today, CancellationToken cancellationToken)
    {
        List<DateOnly> due;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
            var refresh =
                scope.ServiceProvider.GetRequiredService<HolderIntelligenceRefreshService>();
            var quarters = ComputableQuarters(refresh, today);
            if (quarters.Count == 0)
                return;
            await EnsureQuarters(dbContext, quarters, cancellationToken);
            var now = DateTime.UtcNow;
            var cutoff = now - Cooldown;
            var first = quarters[0];
            var latest = quarters[^1];
            var states = await dbContext
                .Set<HolderIntelligenceQuarterState>()
                .Where(state => state.ReportDate >= first && state.ReportDate <= latest)
                .OrderBy(state => state.ReportDate)
                .Select(state => new
                {
                    state.ReportDate,
                    state.DirtyAt,
                    state.ComputedAt,
                    state.CalculationVersion,
                })
                .ToListAsync(cancellationToken);
            var historical = states.Where(state => state.ReportDate < latest).ToList();
            var passStart = HistoricalPassStart(historical.Select(state => state.ComputedAt), now);
            // An uncomputed or outdated quarter never waits for a pass, so a version bump still
            // recomputes the whole history at once.
            due = states
                .Where(state =>
                    state.DirtyAt != null
                    && state.DirtyAt < cutoff
                    && (
                        state.ReportDate == latest
                        || state.ComputedAt == null
                        || state.CalculationVersion
                            < HolderIntelligenceRefreshService.CalculationVersion
                        || state.ComputedAt < passStart
                    )
                )
                .Select(state => state.ReportDate)
                .ToList();
            var resting = historical.Count(state =>
                state.DirtyAt != null && state.DirtyAt < cutoff && !due.Contains(state.ReportDate)
            );
            if (resting > 0 && passStart == null)
                LogRest(resting, historical.Max(state => state.ComputedAt).Value);
        }

        foreach (var reportDate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await WaitsForReplay(cancellationToken))
                return;
            await DrainQuarter(reportDate, today, cancellationToken);
        }
    }

    // Checked before every quarter, since a tick that drains a long cascade outlasts the moment a
    // replay starts.
    private async Task<bool> WaitsForReplay(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var queuedAt = await scope
            .ServiceProvider.GetRequiredService<ProcessedDataSetRepository>()
            .GetRealtimeReplayQueuedAt(cancellationToken);
        if (queuedAt is not { } since || DateTime.UtcNow - since >= ReplayHold)
        {
            _heldForReplayQueuedAt = null;
            return false;
        }
        if (_heldForReplayQueuedAt != since)
            _logger.LogInformation(
                "Holder intelligence drain waits for the 13F bulk replay queued at {QueuedAt:u}",
                since
            );
        _heldForReplayQueuedAt = since;
        return true;
    }

    /// <summary>
    /// When the running pass over the quarters before the latest closed one began, so a quarter it
    /// already rebuilt waits for the next pass, or null while they rest. Read from the stored
    /// rebuild times, so a worker restart resumes the pass or the rest.
    /// </summary>
    internal DateTime? HistoricalPassStart(IEnumerable<DateTime?> computedAt, DateTime now)
    {
        var recent = computedAt.Where(at => at > now - HistoricalPassInterval).ToList();
        if (recent.Count == 0)
            return now;
        var start = recent.Min().Value;
        return now - start < HistoricalPassWindow ? start : null;
    }

    private void LogRest(int resting, DateTime lastRebuild)
    {
        var resumesAt = lastRebuild + HistoricalPassInterval;
        if (_restLoggedUntil == resumesAt)
            return;
        _restLoggedUntil = resumesAt;
        _logger.LogInformation(
            "Holder intelligence drain rests {Count} dirty historical quarters until {ResumesAt:u}",
            resting,
            resumesAt
        );
    }

    private async Task DrainQuarter(
        DateOnly reportDate,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var states = dbContext.Set<HolderIntelligenceQuarterState>();
        // Read the stamp now, not when the tick listed the quarter: an earlier quarter's rebuild
        // in this tick may have re-stamped it, and that stamp is what this rebuild answers.
        var dirtyAt = await states
            .Where(state => state.ReportDate == reportDate)
            .Select(state => state.DirtyAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (dirtyAt == null)
            return;

        try
        {
            await BeforeRebuild(reportDate);
            var rebuilt = await scope
                .ServiceProvider.GetRequiredService<HolderIntelligenceRefreshService>()
                .RebuildQuarter(reportDate, today, cancellationToken);
            if (!rebuilt)
                return;
            await states
                .Where(state => state.ReportDate == reportDate && state.DirtyAt == dirtyAt)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(state => state.DirtyAt, (DateTime?)null)
                            .SetProperty(state => state.ComputedAt, DateTime.UtcNow)
                            .SetProperty(
                                state => state.CalculationVersion,
                                HolderIntelligenceRefreshService.CalculationVersion
                            )
                            .SetProperty(state => state.FailureCount, 0)
                            .SetProperty(state => state.LastError, (string)null),
                    cancellationToken
                );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Holder intelligence rebuild failed for {ReportDate}; retrying after the cooldown",
                reportDate
            );
            await states
                .Where(state => state.ReportDate == reportDate && state.DirtyAt == dirtyAt)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(state => state.DirtyAt, DateTime.UtcNow)
                            .SetProperty(
                                state => state.FailureCount,
                                state => state.FailureCount + 1
                            )
                            .SetProperty(
                                state => state.LastError,
                                ex.GetBaseException().Message.TruncateToFit(2000)
                            ),
                    CancellationToken.None
                );
        }
    }

    // Test seam for a stamp written between reading it and rebuilding.
    protected virtual Task BeforeRebuild(DateOnly reportDate) => Task.CompletedTask;

    // Every computable quarter has a state row, born due; a version bump dirties computed rows.
    private static async Task EnsureQuarters(
        EquiblesFinancialDbContext dbContext,
        List<DateOnly> quarters,
        CancellationToken cancellationToken
    )
    {
        await dbContext
            .Set<HolderIntelligenceQuarterState>()
            .UpsertRange(
                quarters.Select(quarter => new HolderIntelligenceQuarterState
                {
                    ReportDate = quarter,
                    DirtyAt = DateTime.UnixEpoch,
                })
            )
            .On(state => state.ReportDate)
            .NoUpdate()
            .RunAsync(cancellationToken);
        await dbContext
            .Set<HolderIntelligenceQuarterState>()
            .Where(state =>
                state.DirtyAt == null
                && state.CalculationVersion < HolderIntelligenceRefreshService.CalculationVersion
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(state => state.DirtyAt, DateTime.UnixEpoch),
                cancellationToken
            );
    }

    private static List<DateOnly> ComputableQuarters(
        HolderIntelligenceRefreshService refresh,
        DateOnly today
    )
    {
        var quarters = new List<DateOnly>();
        for (
            var quarter = refresh.FirstComputableQuarter;
            refresh.IsComputable(quarter, today);
            quarter = NextQuarter(quarter)
        )
            quarters.Add(quarter);
        return quarters;
    }

    private static DateOnly NextQuarter(DateOnly quarter) =>
        quarter.AddDays(1).AddMonths(3).AddDays(-1);
}
