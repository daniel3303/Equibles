using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Drains dirty AUM quarterly snapshots on a fixed tick: every
/// <see cref="TickInterval"/>, rebuild any snapshot whose <c>DirtyAt</c> is
/// older than <see cref="Cooldown"/>. Paired with
/// <see cref="Consumers.Filings13FImportedConsumer"/>, which just marks
/// <c>DirtyAt = UtcNow</c> on each import — many events for the same quarter
/// in the same cooldown window coalesce into a single rebuild here.
///
/// The dirty flag is claimed with a short future-dated lease before rebuilding.
/// It stays non-null so request paths never mistake the old snapshot for clean.
/// The consumer replaces a future lease with its new event timestamp, which
/// preserves an import that lands mid-rebuild; the successful rebuild clears
/// the lease only when no newer event replaced it. A crashed worker's lease
/// becomes eligible again after the lease and ordinary cooldown expire.
/// </summary>
public class AumSnapshotDrainWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HoldingsAggregateRefreshService _refreshService;
    private readonly ILogger<AumSnapshotDrainWorker> _logger;

    // Virtual seams so tests can collapse the waits without changing
    // production behaviour.
    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(1);
    protected virtual TimeSpan TickInterval => TimeSpan.FromMinutes(5);
    protected virtual TimeSpan Cooldown => TimeSpan.FromHours(1);
    protected virtual TimeSpan ClaimLease => TimeSpan.FromMinutes(5);
    protected virtual TimeSpan ClaimRenewInterval => TimeSpan.FromMinutes(1);

    public AumSnapshotDrainWorker(
        IServiceScopeFactory scopeFactory,
        HoldingsAggregateRefreshService refreshService,
        ILogger<AumSnapshotDrainWorker> logger
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnce(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AUM snapshot drain tick failed; will retry on next interval");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task DrainOnce(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - Cooldown;

        List<DueRebuild> due;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
            due = await dbContext
                .Set<AumQuarterlySnapshot>()
                .Where(s => s.DirtyAt != null && s.DirtyAt < cutoff)
                .Select(s => new DueRebuild
                {
                    ReportDate = s.ReportDate,
                    DirtyAt = s.DirtyAt!.Value,
                })
                .ToListAsync(cancellationToken);
        }

        if (due.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Draining {Count} dirty AUM snapshot(s) past cooldown", due.Count);

        foreach (var entry in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Claim with a future timestamp rather than clearing DirtyAt. Readers keep
            // treating the snapshot as dirty, another drain cannot select it, and a new
            // import replaces the future marker with its real event time. A skipped claim
            // means another worker or a fresh event moved the flag.
            var claimToken = await TryClaimDirtyFlag(entry, cancellationToken);
            if (claimToken is not { } claimedAt)
                continue;
            entry.ClaimedAt = claimedAt;
            using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            var renewal = RenewClaimLease(entry, renewalCts.Token);

            try
            {
                await _refreshService.RebuildQuarterAsync(entry.ReportDate, cancellationToken);
                renewalCts.Cancel();
                await renewal;
                if (!await TryClearActiveClaim(entry, cancellationToken))
                {
                    // The claim expired before it could be renewed, or a new event replaced
                    // it. Re-arm only when our exact token is still present; a newer event's
                    // timestamp never matches and remains untouched.
                    await RearmDirtyFlag(entry, CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
                renewalCts.Cancel();
                await renewal;
                await RearmDirtyFlag(entry, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                renewalCts.Cancel();
                await renewal;
                _logger.LogError(
                    ex,
                    "Failed to drain dirty AUM snapshot for {ReportDate}; will retry on next tick",
                    entry.ReportDate
                );
                await RearmDirtyFlag(entry, cancellationToken);
            }
        }
    }

    // Replaces the selected event timestamp with a future-dated lease and returns that
    // compare-and-clear token. PostgreSQL serializes both uses of the same DateTime value
    // at identical precision, so an equality guard safely distinguishes a newer import.
    private async Task<DateTime?> TryClaimDirtyFlag(
        DueRebuild entry,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var claimedAt = DateTime.UtcNow.Add(ClaimLease);
        var claimed = await dbContext
            .Set<AumQuarterlySnapshot>()
            .Where(s => s.ReportDate == entry.ReportDate && s.DirtyAt == entry.DirtyAt)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DirtyAt, claimedAt), cancellationToken);

        return claimed > 0 ? claimedAt : null;
    }

    // Extend an owned lease while a long aggregate rebuild is running. Failure is safe: the
    // completion path refuses to clear an expired token and re-arms it for another attempt.
    private async Task RenewClaimLease(DueRebuild entry, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ClaimRenewInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var currentClaim = entry.ClaimedAt!.Value;
            var renewedClaim = DateTime.UtcNow.Add(ClaimLease);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dbContext =
                    scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
                var renewed = await dbContext
                    .Set<AumQuarterlySnapshot>()
                    .Where(s =>
                        s.ReportDate == entry.ReportDate
                        && s.DirtyAt == currentClaim
                        && s.DirtyAt > DateTime.UtcNow
                    )
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.DirtyAt, renewedClaim),
                        cancellationToken
                    );
                if (renewed == 0)
                    return;
                entry.ClaimedAt = renewedClaim;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to renew AUM snapshot claim for {ReportDate}; completion will preserve the dirty flag",
                    entry.ReportDate
                );
                return;
            }
        }
    }

    // A successful rebuild clears only its own still-active lease. Npgsql translates
    // DateTime.UtcNow to database now(), so expiry and compare-and-clear are one atomic UPDATE.
    // An import during the rebuild replaces the future marker with a real event timestamp; an
    // expired marker is also refused because stampers can no longer identify it as a lease.
    private async Task<bool> TryClearActiveClaim(
        DueRebuild entry,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var cleared = await dbContext
            .Set<AumQuarterlySnapshot>()
            .Where(s =>
                s.ReportDate == entry.ReportDate
                && s.DirtyAt == entry.ClaimedAt
                && s.DirtyAt > DateTime.UtcNow
            )
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.DirtyAt, (DateTime?)null),
                cancellationToken
            );
        return cleared > 0;
    }

    // Restores the original event timestamp after a failed rebuild — unless an import
    // already replaced the lease with a fresh timestamp that supersedes ours.
    // The restored value is already past the cooldown, so the retry is immediate.
    private async Task RearmDirtyFlag(DueRebuild entry, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

            await dbContext
                .Set<AumQuarterlySnapshot>()
                .Where(s => s.ReportDate == entry.ReportDate && s.DirtyAt == entry.ClaimedAt)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.DirtyAt, entry.DirtyAt),
                    cancellationToken
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Losing the re-arm only delays this quarter until its next import
            // event or the daily safety-net rebuild; never abort the drain loop.
            _logger.LogWarning(
                ex,
                "Failed to re-arm DirtyAt for {ReportDate} after a failed rebuild",
                entry.ReportDate
            );
        }
    }

    private sealed class DueRebuild
    {
        public DateOnly ReportDate { get; set; }
        public DateTime DirtyAt { get; set; }
        public DateTime? ClaimedAt { get; set; }
    }
}
