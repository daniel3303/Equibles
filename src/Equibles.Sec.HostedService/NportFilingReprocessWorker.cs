using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Continuously brings NPORT-P filings up to the current holdings-parser version. Each cycle drains
/// every filing whose <see cref="Equibles.Sec.Data.Models.NportFiling.ParserVersion"/> sits below
/// <see cref="Equibles.Sec.Data.Models.NportFiling.CurrentParserVersion"/> — re-fetching the
/// submission from EDGAR and re-deriving its schedule of holdings. The work is version-driven and
/// resumable, so it survives restarts and automatically re-enrolls every filing after a
/// parser-version bump — no manual trigger needed.
///
/// Runs in the worker process so it shares the single SEC rate-limiter with the other EDGAR
/// scrapers, and starts after a stagger so its initial EDGAR burst doesn't collide with them at
/// deploy time.
/// </summary>
public class NportFilingReprocessWorker : BaseScraperWorker
{
    protected override string WorkerName => "NPORT-P filing reprocess";
    protected override ErrorSource ErrorSource => ErrorSource.NportReprocess;

    // Once the backlog is drained each cycle finds nothing and idles; a periodic re-check is only
    // meaningful to pick up filings left pending after a transient failure or a parser-version bump.
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);

    // Stagger past deploy so the initial EDGAR burst doesn't collide with the other SEC scrapers
    // starting at the same time.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(5);

    public NportFilingReprocessWorker(
        ILogger<NportFilingReprocessWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<NportFilingReprocessManager>();

        var result = await manager.Run(stoppingToken);

        if (result.Processed > 0)
            Logger.LogInformation("NPORT-P filing reprocess cycle: {Summary}", result.Summary);
    }
}
