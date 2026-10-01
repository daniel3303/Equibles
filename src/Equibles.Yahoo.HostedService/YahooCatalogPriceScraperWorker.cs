using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Yahoo.HostedService;

public class YahooCatalogPriceScraperWorker(
    ILogger<YahooCatalogPriceScraperWorker> logger,
    IServiceScopeFactory scopeFactory,
    ErrorReporter errorReporter
) : BaseScraperWorker(logger, scopeFactory, errorReporter)
{
    protected override string WorkerName => "Yahoo catalog price scraper";
    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);
    protected override ErrorSource ErrorSource => ErrorSource.YahooPriceScraper;

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<YahooPriceImportService>();
        await importer.ImportCatalogPrices(stoppingToken);
        if (importer.HasCatalogPriceBacklog || importer.HasEnrichmentBacklog)
            RequestImmediateContinuation();
    }
}
