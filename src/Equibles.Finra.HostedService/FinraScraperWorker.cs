using Equibles.Core;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.HostedService.Configuration;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Worker;
using Microsoft.Extensions.Options;

namespace Equibles.Finra.HostedService;

public class FinraScraperWorker : BaseScraperWorker {
    protected override string WorkerName => "FINRA scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinraScraper;

    public FinraScraperWorker(
        ILogger<FinraScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FinraScraperOptions> options
    ) : base(logger, scopeFactory, errorReporter) {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override bool ValidateConfiguration() {
        using var scope = ScopeFactory.CreateScope();
        var finraClient = scope.ServiceProvider.GetRequiredService<IFinraClient>();
        if (!finraClient.IsConfigured) {
            Logger.LogWarning("FINRA Scraper stopped: FINRA API credentials not configured.");
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        Logger.LogInformation("Starting daily short volume import");
        using (var scope = ScopeFactory.CreateScope()) {
            var shortVolumeService = scope.ServiceProvider.GetRequiredService<ShortVolumeImportService>();
            await shortVolumeService.Import(stoppingToken);
        }

        GarbageCollectorUtil.ForceAggressiveCollection();

        Logger.LogInformation("Starting short interest import");
        using (var scope = ScopeFactory.CreateScope()) {
            var shortInterestService = scope.ServiceProvider.GetRequiredService<ShortInterestImportService>();
            await shortInterestService.Import(stoppingToken);
        }
    }
}
