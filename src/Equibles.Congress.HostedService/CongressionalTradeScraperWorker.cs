using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.Worker;

namespace Equibles.Congress.HostedService;

public class CongressionalTradeScraperWorker : BaseScraperWorker {
    protected override string WorkerName => "Congressional trade scraper";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(12);
    protected override ErrorSource ErrorSource => ErrorSource.CongressScraper;

    public CongressionalTradeScraperWorker(
        ILogger<CongressionalTradeScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    ) : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        using var scope = ScopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<CongressionalTradeSyncService>();
        await syncService.SyncAll(stoppingToken);
        Logger.LogInformation("Congressional trade sync completed");
    }
}
