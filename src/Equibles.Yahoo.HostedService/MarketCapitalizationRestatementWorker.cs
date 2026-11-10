using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Yahoo.HostedService;

// Keeps every stored market cap on its listing's newest traded close, whichever writer stored the bar.
public class MarketCapitalizationRestatementWorker(
    ILogger<MarketCapitalizationRestatementWorker> logger,
    IServiceScopeFactory scopeFactory,
    ErrorReporter errorReporter
) : BaseScraperWorker(logger, scopeFactory, errorReporter)
{
    protected override string WorkerName => "Market cap restatement";
    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);
    protected override ErrorSource ErrorSource => ErrorSource.YahooPriceScraper;

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<MarketCapitalizationRestatementService>()
            .Restate(stoppingToken);
    }
}
