using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.HostedService.Configuration;
using Equibles.Fred.HostedService.Services;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Worker;
using Microsoft.Extensions.Options;

namespace Equibles.Fred.HostedService;

public class FredScraperWorker : BaseScraperWorker {
    protected override string WorkerName => "FRED scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FredScraper;

    public FredScraperWorker(
        ILogger<FredScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FredScraperOptions> options
    ) : base(logger, scopeFactory, errorReporter) {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override bool ValidateConfiguration() {
        using var scope = ScopeFactory.CreateScope();
        var fredClient = scope.ServiceProvider.GetRequiredService<IFredClient>();
        if (!fredClient.IsConfigured) {
            Logger.LogWarning("FRED Scraper stopped: FRED__ApiKey not configured. Set it in your .env file.");
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importService = scope.ServiceProvider.GetRequiredService<FredImportService>();
        await importService.Import(stoppingToken);
    }
}
