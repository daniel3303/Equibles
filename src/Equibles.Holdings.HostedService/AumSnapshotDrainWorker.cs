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
/// Clearing the dirty flag uses optimistic concurrency: we capture the
/// <c>DirtyAt</c> value at claim time and clear it only if it still matches
/// after the rebuild finishes. If a new event arrived mid-rebuild it updated
/// <c>DirtyAt</c> to a different (more recent) timestamp; the conditional
/// clear is a no-op and the next tick rebuilds again, so no signal is lost.
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
            try
            {
                await _refreshService.RebuildQuarterAsync(entry.ReportDate, cancellationToken);
                await ClearDirtyFlag(entry, cancellationToken);
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
            }
        }
    }

    private async Task ClearDirtyFlag(DueRebuild entry, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        // Optimistic concurrency clear: only nullify DirtyAt if it still
        // matches the timestamp we captured at claim time. If a new event
        // landed mid-rebuild, DirtyAt was overwritten and this is a no-op,
        // so the next tick rebuilds again — no signal lost.
        var cleared = await dbContext
            .Set<AumQuarterlySnapshot>()
            .Where(s => s.ReportDate == entry.ReportDate && s.DirtyAt == entry.DirtyAt)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.DirtyAt, (DateTime?)null),
                cancellationToken
            );

        if (cleared == 0)
        {
            _logger.LogDebug(
                "DirtyAt for {ReportDate} changed during rebuild; leaving it set for the next drain tick",
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
