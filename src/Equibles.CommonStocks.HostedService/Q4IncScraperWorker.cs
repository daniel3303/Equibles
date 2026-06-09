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
/// Periodically scrapes news and events from the IR sites of stocks classified as
/// running on the Q4 Inc platform, filling IrNewsItem / IrEvent.
/// </summary>
public class Q4IncScraperWorker : BaseScraperWorker
{
    protected override string WorkerName => "Q4 Inc IR scrape";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.InvestorRelationsScraper;

    public Q4IncScraperWorker(
        ILogger<Q4IncScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<Q4IncScraperOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override Task DoWork(CancellationToken stoppingToken) =>
        RunImport<Q4IncScraperService>(stoppingToken);
}
