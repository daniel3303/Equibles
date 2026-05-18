using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Equibles.Worker;

public abstract class BaseScraperWorker : BackgroundService
{
    protected readonly ILogger Logger;
    protected readonly IServiceScopeFactory ScopeFactory;
    protected readonly ErrorReporter ErrorReporter;

    private bool _retrySoonRequested;

    protected abstract string WorkerName { get; }
    protected abstract TimeSpan SleepInterval { get; }
    protected abstract ErrorSource ErrorSource { get; }

    /// <summary>
    /// Wait used after a cycle that called <see cref="RequestRetrySoon"/> —
    /// e.g. a cold start where a dependency (the tracked-stock universe) isn't
    /// populated yet. Short by design so the scraper recovers in minutes
    /// instead of sleeping the full <see cref="SleepInterval"/>.
    /// </summary>
    protected virtual TimeSpan NotReadyRetryInterval => TimeSpan.FromMinutes(2);

    /// <summary>
    /// Marks the just-finished cycle as a no-op caused by a not-yet-ready
    /// dependency, so the loop waits <see cref="NotReadyRetryInterval"/>
    /// instead of <see cref="SleepInterval"/>. Reset at the start of each cycle.
    /// </summary>
    protected void RequestRetrySoon() => _retrySoonRequested = true;

    protected BaseScraperWorker(
        ILogger logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
    {
        Logger = logger;
        ScopeFactory = scopeFactory;
        ErrorReporter = errorReporter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ValidateConfiguration())
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            Logger.LogInformation("{Worker} running at: {Time}", WorkerName, DateTimeOffset.Now);
            _retrySoonRequested = false;

            try
            {
                await DoWork(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                Logger.LogInformation("{Worker} cancelled", WorkerName);
                return;
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "Critical error in {Worker}", WorkerName);
                await ErrorReporter.Report(
                    ErrorSource,
                    $"{WorkerName}.DoWork",
                    ex.Message,
                    ex.StackTrace
                );
            }

            var interval = _retrySoonRequested ? NotReadyRetryInterval : SleepInterval;
            Logger.LogInformation(
                _retrySoonRequested
                    ? "{Worker} not ready (dependency pending); retrying in {Interval}"
                    : "{Worker} cycle complete. Sleeping for {Interval}",
                WorkerName,
                interval
            );
            await Task.Delay(interval, stoppingToken);
        }
    }

    protected virtual bool ValidateConfiguration() => true;

    protected abstract Task DoWork(CancellationToken stoppingToken);
}
