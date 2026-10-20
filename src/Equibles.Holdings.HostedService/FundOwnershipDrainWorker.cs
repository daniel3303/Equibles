using Equibles.Core.Extensions;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Keeps every computable quarter's registered-fund ownership current. Each tick reads the
/// quarters' input fingerprints in one pass and rebuilds, newest first and at most
/// <see cref="MaxQuartersPerTick"/> at a time, the quarters published by an older
/// <see cref="FundOwnershipRefreshService.CalculationVersion"/>, from other inputs, or more than
/// <see cref="FundOwnershipRefreshService.RefreshAfter"/> ago. A failed quarter waits
/// <see cref="RetryAfter"/> before its next attempt.
/// </summary>
public class FundOwnershipDrainWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FundOwnershipDrainWorker> _logger;

    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(3);
    protected virtual TimeSpan TickInterval => TimeSpan.FromMinutes(30);

    /// <summary>Quarters rebuilt per tick, so a version bump backfills over hours, not in one burst.</summary>
    public const int MaxQuartersPerTick = 2;

    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    public FundOwnershipDrainWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<FundOwnershipDrainWorker> logger
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
                    _logger.LogError(ex, "Fund ownership drain tick failed; retrying");
                }
                await Task.Delay(TickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
    }

    /// <summary>Rebuilds this tick's due quarters and returns how many it published.</summary>
    internal async Task<int> DrainOnce(DateOnly today, CancellationToken cancellationToken)
    {
        List<(DateOnly Quarter, string Fingerprint)> due;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var refresh = scope.ServiceProvider.GetRequiredService<FundOwnershipRefreshService>();
            var quarters = refresh.ComputableQuarters(today);
            if (quarters.Count == 0)
                return 0;
            var fingerprints = await refresh.Fingerprints(quarters, cancellationToken);
            var states = await scope
                .ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>()
                .Set<FundOwnershipQuarterState>()
                .AsNoTracking()
                .ToDictionaryAsync(state => state.ReportDate, cancellationToken);
            var now = DateTime.UtcNow;
            due = quarters
                .Where(quarter =>
                    IsDue(states.GetValueOrDefault(quarter), fingerprints[quarter], now)
                )
                .Take(MaxQuartersPerTick)
                .Select(quarter => (quarter, fingerprints[quarter]))
                .ToList();
        }

        var published = 0;
        foreach (var (quarter, fingerprint) in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await DrainQuarter(quarter, fingerprint, today, cancellationToken))
                published++;
        }
        return published;
    }

    /// <summary>
    /// Whether a quarter needs a rebuild: never published, published by another version or from
    /// other inputs, or stale; a quarter that just failed waits out <see cref="RetryAfter"/>.
    /// </summary>
    internal static bool IsDue(FundOwnershipQuarterState state, string fingerprint, DateTime now)
    {
        if (state == null)
            return true;
        if (state.FailureCount > 0 && state.AttemptedAt > now - RetryAfter)
            return false;
        return state.ComputedAt == null
            || state.CalculationVersion != FundOwnershipRefreshService.CalculationVersion
            || state.InputFingerprint != fingerprint
            || state.ComputedAt < now - FundOwnershipRefreshService.RefreshAfter;
    }

    private async Task<bool> DrainQuarter(
        DateOnly quarter,
        string fingerprint,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<FundOwnershipRefreshService>()
                .RebuildQuarter(quarter, fingerprint, today, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Fund ownership rebuild failed for {ReportDate}; retrying after {RetryAfter}",
                quarter,
                RetryAfter
            );
            await RecordFailure(quarter, ex);
            return false;
        }
    }

    private async Task RecordFailure(DateOnly quarter, Exception ex)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var states = scope
            .ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>()
            .Set<FundOwnershipQuarterState>();
        await states
            .Upsert(new FundOwnershipQuarterState { ReportDate = quarter })
            .On(state => state.ReportDate)
            .NoUpdate()
            .RunAsync(CancellationToken.None);
        var now = DateTime.UtcNow;
        await states
            .Where(state => state.ReportDate == quarter)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(state => state.AttemptedAt, now)
                        .SetProperty(state => state.FailureCount, state => state.FailureCount + 1)
                        .SetProperty(
                            state => state.LastError,
                            ex.GetBaseException().Message.TruncateToFit(2000)
                        ),
                CancellationToken.None
            );
    }
}
