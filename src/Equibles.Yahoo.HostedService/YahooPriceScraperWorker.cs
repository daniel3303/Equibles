using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Yahoo.HostedService;

public class YahooPriceScraperWorker : BaseScraperWorker {
    protected override string WorkerName => "Yahoo price scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.YahooPriceScraper;

    public YahooPriceScraperWorker(
        ILogger<YahooPriceScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<YahooPriceScraperOptions> options
    ) : base(logger, scopeFactory, errorReporter) {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importService = scope.ServiceProvider.GetRequiredService<YahooPriceImportService>();
        await importService.Import(stoppingToken);
    }
}
