using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Continuously brings insider transactions up to the current parser version.
/// Each cycle drains every filing whose rows sit below
/// <see cref="Equibles.InsiderTrading.Data.Models.InsiderTransaction.CurrentParserVersion"/>
/// — re-deriving security kind, price validity, and footnotes from the cached
/// ownership XML, fetching and caching that XML from EDGAR the first time a filing
/// is seen. The work is version-driven and resumable, so it survives restarts and
/// automatically re-enrolls every filing after a parser-version bump — no manual
/// trigger needed.
///
/// Runs in the worker process so it shares the single SEC rate-limiter with the
/// other EDGAR scrapers (rather than competing as a separate process), and starts
/// after a stagger so it doesn't contend for the request budget at deploy time.
/// </summary>
public class InsiderFilingReprocessWorker : BaseScraperWorker
{
    protected override string WorkerName => "Insider filing reprocess";
    protected override ErrorSource ErrorSource => ErrorSource.InsiderTradingReprocess;

    // Once the backlog is drained each cycle finds nothing and idles; a periodic
    // re-check is only meaningful to pick up rows left pending after a transient
    // failure or a future parser-version bump.
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);

    // Stagger past deploy so the initial EDGAR burst doesn't collide with the
    // other SEC scrapers starting at the same time.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(5);

    public InsiderFilingReprocessWorker(
        ILogger<InsiderFilingReprocessWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<InsiderFilingReprocessManager>();

        var result = await manager.Run(cancellationToken: stoppingToken);

        if (result.Processed > 0)
            Logger.LogInformation("Insider filing reprocess cycle: {Summary}", result.Summary);
    }
}
