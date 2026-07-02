using Equibles.CommonStocks.HostedService.Configuration;
using Equibles.CommonStocks.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.CommonStocks.HostedService;

/// <summary>
/// Periodically discovers each company's website from the registered
/// <c>IWebsiteSource</c> implementations, filling in <c>CommonStock.Website</c>
/// incrementally. Runs upstream of the enrichment that consumes the website,
/// which needs the website to probe for an IR page.
/// </summary>
public class WebsiteDiscoveryWorker : BaseScraperWorker
{
    protected override string WorkerName => "Website discovery";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.WebsiteDiscovery;

    public WebsiteDiscoveryWorker(
        ILogger<WebsiteDiscoveryWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<WebsiteDiscoveryOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<WebsiteDiscoveryService>();

        // Drain a large backlog in successive bursts: when a cycle fills a full batch there are
        // still pending stocks, so chain into the next batch after the short ContinuationInterval
        // instead of sleeping the full SleepInterval (one batch per hour).
        if (await service.DiscoverBatch(stoppingToken))
        {
            RequestImmediateContinuation();
        }
    }
}
