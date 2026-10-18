using Equibles.Core.Extensions;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Rebuilds every computable holder-intelligence quarter whose stamp is past the cooldown, oldest
/// first, including quarters computed by an older calculation version. A stamp is cleared only
/// while it still holds the value read before the rebuild, so a stamp written meanwhile survives.
/// </summary>
public class HolderIntelligenceDrainWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HolderIntelligenceDrainWorker> _logger;

    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(2);
    protected virtual TimeSpan TickInterval => TimeSpan.FromMinutes(5);
    protected virtual TimeSpan Cooldown => HolderIntelligenceRefreshService.Cooldown;

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
            var cutoff = DateTime.UtcNow - Cooldown;
            var first = quarters[0];
            var latest = quarters[^1];
            due = await dbContext
                .Set<HolderIntelligenceQuarterState>()
                .Where(state =>
                    state.DirtyAt != null
                    && state.DirtyAt < cutoff
                    && state.ReportDate >= first
                    && state.ReportDate <= latest
                )
                .OrderBy(state => state.ReportDate)
                .Select(state => state.ReportDate)
                .ToListAsync(cancellationToken);
        }

        foreach (var reportDate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DrainQuarter(reportDate, today, cancellationToken);
        }
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
