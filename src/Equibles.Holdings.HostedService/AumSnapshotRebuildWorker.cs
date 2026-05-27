using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Daily safety-net for the per-quarter AUM and sector-allocation snapshots
/// that power /holdings/stats and /holdings/trends.
///
/// The event-driven path (<see cref="Consumers.Filings13FImportedConsumer"/>)
/// keeps snapshots current on every 13F import. This worker is the second
/// line of defence — it rebuilds every quarter once a day so a message lost
/// to a transient bus failure, or a snapshot row missed during a bulk
/// historical replay, gets reconciled within 24h.
///
/// On boot, the worker runs a one-off backfill with an extended
/// <c>CommandTimeout</c> whenever the snapshot tables don't yet cover every
/// quarter present in the holdings table. The naive "snapshot tables empty"
/// gate this replaced lost the backfill whenever the realtime consumer beat
/// the worker to inserting the first row.
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
                "AUM snapshot first-boot backfill failed; daily safety-net will retry full rebuild on each cycle"
            );
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Running daily AUM snapshot safety-net rebuild");
                await _refreshService.RebuildAllAsync(stoppingToken);
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
                await Task.Delay(SleepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task TryBackfillIfNeeded(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var holdingQuarters = await dbContext
            .Set<InstitutionalHolding>()
            .Select(h => h.ReportDate)
            .Distinct()
            .CountAsync(cancellationToken);
        if (holdingQuarters == 0)
        {
            return;
        }

        var snapshotQuarters = await dbContext
            .Set<AumQuarterlySnapshot>()
            .CountAsync(cancellationToken);
        if (snapshotQuarters >= holdingQuarters)
        {
            return;
        }

        _logger.LogInformation(
            "AUM snapshot coverage incomplete ({Snapshots}/{Holdings} quarters) — running backfill with {Timeout}s command timeout",
            snapshotQuarters,
            holdingQuarters,
            BackfillCommandTimeout.TotalSeconds
        );

        await _refreshService.RebuildAllAsync(BackfillCommandTimeout, cancellationToken);
    }
}
