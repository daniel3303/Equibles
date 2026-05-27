using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Messaging.Contracts.Activity;
using MassTransit;
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
    /// Resolves the bus lazily — direct <see cref="IBus.Publish{T}"/> calls
    /// don't go through the EF outbox, so activity events stay fire-and-forget
    /// (a dropped event is fine; a stuck transaction would be worse). Returns
    /// null if no bus is registered, so tests and any host that wires the
    /// worker without messaging still run.
    /// </summary>
    private IBus TryGetBus()
    {
        using var scope = ScopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<IBus>();
    }

    private async Task PublishActivity(
        ScraperActivitySeverity severity,
        string message,
        CancellationToken cancellationToken
    )
    {
        var bus = TryGetBus();
        if (bus is null)
            return;

        try
        {
            await bus.Publish(
                new ScraperActivity(
                    Source: WorkerName,
                    Severity: severity,
                    Message: message,
                    Timestamp: DateTimeOffset.UtcNow,
                    CorrelationId: Guid.NewGuid().ToString()
                ),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down — swallow.
        }
        catch (Exception ex)
        {
            // The activity feed is best-effort. A failure here must never
            // crash the scraper; log at Debug so it shows under verbose
            // logging but doesn't pollute normal runs.
            Logger.LogDebug(ex, "Failed to publish ScraperActivity for {Worker}", WorkerName);
        }
    }

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

        // One-off stagger before the first cycle. Workers sharing a rate-limited
        // upstream (SEC EDGAR) use this so they don't all fire at deploy time and
        // exhaust the shared request budget — see the SEC scrapers' StartupDelay.
        if (StartupDelay > TimeSpan.Zero)
        {
            Logger.LogInformation(
                "{Worker} staggering startup by {Delay}",
                WorkerName,
                FormatInterval(StartupDelay)
            );
        }
        try
        {
            await DelayStartup(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            Logger.LogInformation("{Worker} running at: {Time}", WorkerName, DateTimeOffset.Now);
            _retrySoonRequested = false;

            await PublishActivity(ScraperActivitySeverity.Info, "cycle started", stoppingToken);

            try
            {
                await DoWork(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                Logger.LogInformation("{Worker} cancelled", WorkerName);
                await PublishActivity(
                    ScraperActivitySeverity.Warn,
                    "cancelled",
                    CancellationToken.None
                );
                return;
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "Critical error in {Worker}", WorkerName);
                // ErrorReporter publishes its own ScraperActivity with the same
                // source + the error message, so the activity feed gets a row
                // without double-emitting here.
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
            await PublishActivity(
                _retrySoonRequested ? ScraperActivitySeverity.Warn : ScraperActivitySeverity.Info,
                _retrySoonRequested
                    ? $"not ready, retrying in {FormatInterval(interval)}"
                    : $"cycle complete, sleeping {FormatInterval(interval)}",
                stoppingToken
            );
            await WaitForNextCycle(interval, stoppingToken);
        }
    }

    private static string FormatInterval(TimeSpan interval)
    {
        if (interval.TotalHours >= 1)
            return $"{interval.TotalHours:0.#}h";
        if (interval.TotalMinutes >= 1)
            return $"{interval.TotalMinutes:0.#}m";
        return $"{interval.TotalSeconds:0}s";
    }

    /// <summary>
    /// Waits between cycles. Default is a plain delay; a worker can override to
    /// also wake early on an external signal (e.g. <c>HoldingsScraperWorker</c>
    /// waking on a CUSIP-change rescan request).
    /// </summary>
    protected virtual Task WaitForNextCycle(TimeSpan interval, CancellationToken stoppingToken) =>
        Task.Delay(interval, stoppingToken);

    /// <summary>
    /// One-off delay before the first cycle (default none). Lets workers that
    /// share a rate-limited upstream be staggered at startup so they don't all
    /// fire at once — the SEC scrapers set this so the lighter, time-sensitive
    /// 13F real-time sweep gets the SEC request budget first after a deploy.
    /// </summary>
    protected virtual TimeSpan StartupDelay => TimeSpan.Zero;

    /// <summary>Awaits <see cref="StartupDelay"/>; overridable so tests can gate it deterministically.</summary>
    protected virtual Task DelayStartup(CancellationToken stoppingToken) =>
        StartupDelay > TimeSpan.Zero ? Task.Delay(StartupDelay, stoppingToken) : Task.CompletedTask;

    protected virtual bool ValidateConfiguration() => true;

    protected abstract Task DoWork(CancellationToken stoppingToken);
}
