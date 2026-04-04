using Equibles.Cftc.HostedService.Configuration;
using Equibles.Cftc.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.Options;

namespace Equibles.Cftc.HostedService;

public class CftcScraperWorker : BaseScraperWorker {
    protected override string WorkerName => "CFTC scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.CftcScraper;

    public CftcScraperWorker(
        ILogger<CftcScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<CftcScraperOptions> options
    ) : base(logger, scopeFactory, errorReporter) {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importService = scope.ServiceProvider.GetRequiredService<CftcImportService>();
        await importService.Import(stoppingToken);
    }
}
