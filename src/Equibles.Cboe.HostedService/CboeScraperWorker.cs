using Equibles.Cboe.HostedService.Configuration;
using Equibles.Cboe.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.Options;

namespace Equibles.Cboe.HostedService;

public class CboeScraperWorker : BaseScraperWorker {
    protected override string WorkerName => "CBOE scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.CboeScraper;

    public CboeScraperWorker(
        ILogger<CboeScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<CboeScraperOptions> options
    ) : base(logger, scopeFactory, errorReporter) {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importService = scope.ServiceProvider.GetRequiredService<CboeImportService>();
        await importService.Import(stoppingToken);
    }
}
