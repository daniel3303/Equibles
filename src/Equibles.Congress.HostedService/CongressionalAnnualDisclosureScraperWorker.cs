using Equibles.Congress.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;

namespace Equibles.Congress.HostedService;

public class CongressionalAnnualDisclosureScraperWorker : BaseScraperWorker
{
    protected override string WorkerName => "Congressional annual disclosure scraper";

    // Annual reports trickle in around filing deadlines; one pass a day keeps
    // the bands current without hammering the Clerk and eFD endpoints.
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected override ErrorSource ErrorSource => ErrorSource.CongressScraper;

    public CongressionalAnnualDisclosureScraperWorker(
        ILogger<CongressionalAnnualDisclosureScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var syncService =
            scope.ServiceProvider.GetRequiredService<CongressionalAnnualDisclosureSyncService>();
        await syncService.SyncAll(stoppingToken);
        Logger.LogInformation("Congressional annual disclosure sync completed");
    }
}
