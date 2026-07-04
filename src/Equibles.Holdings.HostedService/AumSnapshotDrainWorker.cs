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
/// The dirty flag is CLAIMED (cleared) before the rebuild runs, not after.
/// The consumer only stamps <c>DirtyAt</c> when it is currently null, so a
/// clear-after-rebuild could never observe a mid-rebuild import — the flag
/// would test unchanged and the signal be silently dropped. Clearing first
/// means an import landing mid-rebuild finds the flag null, re-dirties the
/// row with a fresh timestamp, and the next cooldown rebuilds again — no
/// signal lost. If the rebuild fails, the claim is re-armed with the original
/// timestamp so the next tick retries immediately.
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

            // Claim BEFORE rebuilding: the consumer only stamps DirtyAt when it
            // is null, so an import landing mid-rebuild can only be observed if
            // the flag is already cleared — it then re-dirties the row with a
            // fresh timestamp and the next cooldown rebuilds again. A skipped
            // claim means another racer (or a fresh event) moved the flag;
            // leave this entry for the next tick.
            if (!await TryClaimDirtyFlag(entry, cancellationToken))
                continue;

            try
            {
                await _refreshService.RebuildQuarterAsync(entry.ReportDate, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to drain dirty AUM snapshot for {ReportDate}; will retry on next tick",
                    entry.ReportDate
                );
                await RearmDirtyFlag(entry, cancellationToken);
            }
        }
    }

    // Clears DirtyAt only if it still matches the claim-time value, returning
    // whether this worker won the claim.
    private async Task<bool> TryClaimDirtyFlag(
        DueRebuild entry,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var claimed = await dbContext
            .Set<AumQuarterlySnapshot>()
            .Where(s => s.ReportDate == entry.ReportDate && s.DirtyAt == entry.DirtyAt)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.DirtyAt, (DateTime?)null),
                cancellationToken
            );

        return claimed > 0;
    }

    // Restores the original claim timestamp after a failed rebuild — unless an
    // import already re-dirtied the row (a fresh timestamp supersedes ours).
    // The restored value is already past the cooldown, so the retry is immediate.
    private async Task RearmDirtyFlag(DueRebuild entry, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

            await dbContext
                .Set<AumQuarterlySnapshot>()
                .Where(s => s.ReportDate == entry.ReportDate && s.DirtyAt == null)
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
    }
}
