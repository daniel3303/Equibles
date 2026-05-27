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
/// On first boot (snapshot tables empty but the holdings table is not), the
/// worker performs a one-off backfill of every existing quarter with an
/// extended <c>CommandTimeout</c> for the initial pass — afterwards the
/// per-quarter work is small enough that the default timeout is fine.
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

        await TryBackfillIfEmpty(stoppingToken);

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

    private async Task TryBackfillIfEmpty(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var snapshotsExist = await dbContext
            .Set<AumQuarterlySnapshot>()
            .AnyAsync(cancellationToken);
        if (snapshotsExist)
        {
            return;
        }

        var hasHoldings = await dbContext.Set<InstitutionalHolding>().AnyAsync(cancellationToken);
        if (!hasHoldings)
        {
            return;
        }

        _logger.LogInformation(
            "AUM snapshot tables are empty but holdings exist — running one-off backfill with {Timeout}s command timeout",
            BackfillCommandTimeout.TotalSeconds
        );

        await _refreshService.RebuildAllAsync(BackfillCommandTimeout, cancellationToken);
    }
}
