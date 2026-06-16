using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Keeps the materialised <see cref="Sec.Data.Models.FundSeries"/> directory current. After a
/// startup delay it rebuilds the whole directory, then again every <see cref="SleepInterval"/>.
/// Funds report monthly, so a daily full rebuild from <see cref="FundSeriesRefreshService"/> is
/// ample — there is no per-filing dirty signal to drain. The first rebuild runs with an extended
/// command timeout because the cold "latest report per series" scan over the whole NPORT table is
/// the heaviest one.
/// </summary>
public class FundSeriesRefreshWorker : BackgroundService
{
    private readonly FundSeriesRefreshService _refreshService;
    private readonly ILogger<FundSeriesRefreshWorker> _logger;

    // Virtual seams so tests can collapse the waits without changing production behaviour.
    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(2);
    protected virtual TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected virtual TimeSpan RebuildCommandTimeout => TimeSpan.FromMinutes(30);

    public FundSeriesRefreshWorker(
        FundSeriesRefreshService refreshService,
        ILogger<FundSeriesRefreshWorker> logger
    )
    {
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
                _logger.LogInformation("Rebuilding fund-series directory");
                await _refreshService.RebuildAllAsync(RebuildCommandTimeout, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // A throw out of ExecuteAsync is fatal to a BackgroundService. Catch everything
                // except cancellation so a transient DB hiccup doesn't kill the worker for the
                // process lifetime — the next cycle reconciles.
                _logger.LogError(ex, "Fund-series directory rebuild failed; will retry next cycle");
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
}
