using Equibles.Congress.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;

namespace Equibles.Congress.HostedService;

public class CongressLegislatorSyncWorker : BaseScraperWorker
{
    protected override string WorkerName => "Congress legislator roster sync";

    // The roster changes when a member is sworn in, leaves or switches party; a weekly read
    // keeps terms current without re-downloading the 13 MB historical file every day.
    protected override TimeSpan SleepInterval => TimeSpan.FromDays(7);
    protected override ErrorSource ErrorSource => ErrorSource.CongressScraper;

    public CongressLegislatorSyncWorker(
        ILogger<CongressLegislatorSyncWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var syncService = scope.ServiceProvider.GetRequiredService<CongressLegislatorSyncService>();
        await syncService.SyncAll(stoppingToken);
        Logger.LogInformation("Congress legislator roster sync completed");
    }
}
