using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Backfills the filer CIK, and any Rule 10b5-1 plan adoption date, onto Form 144 notices
/// imported before those fields were captured.
///
/// Selection is driven entirely by the column being null, so the run drains to nothing and
/// then idles. Once history is filled the parser writes both fields at ingest and this worker
/// has no work left to do.
///
/// Runs in the worker process so it shares the single SEC rate limiter with the other EDGAR
/// scrapers rather than competing with them as a separate process, and starts on a stagger so
/// the initial burst does not collide with them at deploy time.
/// </summary>
public class Form144FilerCikBackfillWorker : BaseScraperWorker
{
    protected override string WorkerName => "Form 144 filer CIK backfill";
    protected override ErrorSource ErrorSource => ErrorSource.InsiderTradingReprocess;

    // Once drained each cycle finds nothing; the periodic re-check only picks up notices left
    // pending by a transient EDGAR failure.
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);

    // Longer than the insider reprocess stagger so the two EDGAR backfills do not start
    // together and split the request budget.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(9);

    public Form144FilerCikBackfillWorker(
        ILogger<Form144FilerCikBackfillWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        using var scope = ScopeFactory.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Form144FilerCikBackfillManager>();

        var resolved = await manager.Run(stoppingToken);

        if (resolved > 0)
            Logger.LogInformation("Resolved the filer CIK on {Count} Form 144 notices", resolved);
    }
}
