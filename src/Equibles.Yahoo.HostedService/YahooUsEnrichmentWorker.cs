using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Yahoo.HostedService;

// Drains US key statistics and company profiles independently of the US price pass; each attempt
// is stamped per listing, so a restart resumes at the next due stock.
public class YahooUsEnrichmentWorker(
    ILogger<YahooUsEnrichmentWorker> logger,
    IServiceScopeFactory scopeFactory,
    ErrorReporter errorReporter
) : BaseScraperWorker(logger, scopeFactory, errorReporter)
{
    protected override string WorkerName => "Yahoo US enrichment";
    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);
    protected override ErrorSource ErrorSource => ErrorSource.YahooPriceScraper;

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<YahooPriceImportService>();
        await importer.ImportUsEnrichment(stoppingToken);
        if (importer.HasEnrichmentBacklog)
            RequestImmediateContinuation();
    }
}
