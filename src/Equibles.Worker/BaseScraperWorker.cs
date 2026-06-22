using System.Globalization;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Messaging.Contracts.Activity;
using MassTransit;
using Microsoft.Extensions.Configuration;
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
    private bool _continuationRequested;
    private int _consecutiveFailures;

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

    /// <summary>
    /// Wait used after a cycle that called <see cref="RequestImmediateContinuation"/> —
    /// e.g. a backfill that filled its batch and still has a backlog queued. Short by
    /// design so a large backlog drains in successive bursts instead of one batch every
    /// <see cref="SleepInterval"/>, while a brief gap between cycles still yields the
    /// shared upstream budget to latency-sensitive workers.
    /// </summary>
    protected virtual TimeSpan ContinuationInterval => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Marks the just-finished cycle as having more work immediately available, so the
    /// loop waits <see cref="ContinuationInterval"/> instead of <see cref="SleepInterval"/>.
    /// Reset at the start of each cycle, and outranked by <see cref="RequestRetrySoon"/> —
    /// a not-yet-ready dependency is a reason to back off, not to press on.
    /// </summary>
    protected void RequestImmediateContinuation() => _continuationRequested = true;

    /// <summary>
    /// Base wait after a cycle that FAULTED (DoWork threw). Short by design so a transient
    /// dependency outage — e.g. the database bouncing during a deploy — costs minutes, not
    /// the full <see cref="SleepInterval"/>. The wait grows exponentially per consecutive
    /// failure (see <see cref="ErrorBackoff"/>), capped by <see cref="MaxErrorBackoffInterval"/>,
    /// and a clean cycle resets the streak. Without this a single throw parked the worker for
    /// a whole <see cref="SleepInterval"/> (up to hours), abandoning any backlog it was draining.
    /// </summary>
    protected virtual TimeSpan ErrorBackoffInterval => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Upper bound for the exponential <see cref="ErrorBackoffInterval"/> growth. A faulted cycle
    /// never waits longer than this — nor longer than <see cref="SleepInterval"/>, whichever is
    /// smaller — so a prolonged outage backs off in bounded steps instead of hammering.
    /// </summary>
    protected virtual TimeSpan MaxErrorBackoffInterval => TimeSpan.FromMinutes(15);

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

    protected async Task RunImport<TImporter>(CancellationToken stoppingToken)
        where TImporter : IImporter
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<TImporter>();
        await importer.Import(stoppingToken);
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
            _continuationRequested = false;
            var faulted = false;

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
                // A faulted cycle backs off briefly and retries instead of sleeping the full
                // SleepInterval — a transient outage (e.g. the DB bouncing during a deploy)
                // must not park the worker for hours. ErrorReporter.Report is best-effort and
                // never throws, so the backoff path below always runs.
                faulted = true;
                _consecutiveFailures++;
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

            TimeSpan interval;
            if (faulted)
            {
                interval = ErrorBackoff();
                Logger.LogWarning(
                    "{Worker} cycle failed ({Failures} in a row); backing off {Interval} before retry",
                    WorkerName,
                    _consecutiveFailures,
                    FormatInterval(interval)
                );
                await PublishActivity(
                    ScraperActivitySeverity.Warn,
                    $"cycle failed, retrying in {FormatInterval(interval)}",
                    stoppingToken
                );
            }
            else
            {
                // A clean cycle clears the failure streak, so the next fault starts
                // again from the base ErrorBackoffInterval.
                _consecutiveFailures = 0;
                if (_retrySoonRequested)
                {
                    interval = NotReadyRetryInterval;
                    Logger.LogInformation(
                        "{Worker} not ready (dependency pending); retrying in {Interval}",
                        WorkerName,
                        interval
                    );
                    await PublishActivity(
                        ScraperActivitySeverity.Warn,
                        $"not ready, retrying in {FormatInterval(interval)}",
                        stoppingToken
                    );
                }
                else if (_continuationRequested)
                {
                    interval = ContinuationInterval;
                    Logger.LogInformation(
                        "{Worker} has more work queued; continuing in {Interval}",
                        WorkerName,
                        interval
                    );
                    await PublishActivity(
                        ScraperActivitySeverity.Info,
                        $"more work queued, continuing in {FormatInterval(interval)}",
                        stoppingToken
                    );
                }
                else
                {
                    interval = SleepInterval;
                    Logger.LogInformation(
                        "{Worker} cycle complete. Sleeping for {Interval}",
                        WorkerName,
                        interval
                    );
                    await PublishActivity(
                        ScraperActivitySeverity.Info,
                        $"cycle complete, sleeping {FormatInterval(interval)}",
                        stoppingToken
                    );
                }
            }
            await WaitForNextCycle(interval, stoppingToken);
        }
    }

    private static string FormatInterval(TimeSpan interval)
    {
        if (interval.TotalHours >= 1)
            return $"{interval.TotalHours.ToString("0.#", CultureInfo.InvariantCulture)}h";
        if (interval.TotalMinutes >= 1)
            return $"{interval.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture)}m";
        return $"{interval.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}s";
    }

    /// <summary>
    /// Exponential backoff for consecutive faulted cycles: <see cref="ErrorBackoffInterval"/>
    /// doubled once per consecutive failure, capped at the smaller of <see cref="SleepInterval"/>
    /// and <see cref="MaxErrorBackoffInterval"/>. Called with <see cref="_consecutiveFailures"/>
    /// already incremented, so the first failure waits exactly one base interval.
    /// </summary>
    private TimeSpan ErrorBackoff()
    {
        var cap = SleepInterval < MaxErrorBackoffInterval ? SleepInterval : MaxErrorBackoffInterval;
        // Cap the doublings so the shift can't overflow a long; this is far past the cap anyway.
        var doublings = Math.Min(_consecutiveFailures - 1, 16);
        var scaledTicks = ErrorBackoffInterval.Ticks * (1L << doublings);
        return TimeSpan.FromTicks(Math.Min(scaledTicks, cap.Ticks));
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

    /// <summary>
    /// Startup gate shared by every SEC-EDGAR-backed scraper: SEC 403-bans a
    /// source whose User-Agent carries no contact email, so a worker with no
    /// usable <c>Sec:ContactEmail</c> must not loop uselessly. Logs a warning and
    /// returns false when the value is absent; returns true otherwise.
    /// </summary>
    /// <param name="treatWhitespaceAsAbsent">
    /// When true a whitespace-only value also fails the gate (its User-Agent
    /// would carry no real contact). Workers disagree on this today — the SEC
    /// scrapers accept whitespace while the Holdings/financial-facts scrapers
    /// reject it — so callers pass their current behavior explicitly to keep it.
    /// </param>
    protected bool ValidateSecContactEmail(
        IConfiguration configuration,
        string label,
        bool treatWhitespaceAsAbsent
    )
    {
        var email = configuration["Sec:ContactEmail"];
        var absent = treatWhitespaceAsAbsent
            ? string.IsNullOrWhiteSpace(email)
            : string.IsNullOrEmpty(email);
        if (absent)
        {
            Logger.LogWarning(
                "{Label} stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file.",
                label
            );
            return false;
        }
        return true;
    }

    protected abstract Task DoWork(CancellationToken stoppingToken);
}
