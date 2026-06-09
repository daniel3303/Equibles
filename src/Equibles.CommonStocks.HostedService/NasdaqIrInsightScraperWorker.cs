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
/// running on Nasdaq IR Insight, filling IrNewsItem / IrEvent.
/// </summary>
public class NasdaqIrInsightScraperWorker : BaseScraperWorker
{
    protected override string WorkerName => "Nasdaq IR Insight scrape";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.InvestorRelationsScraper;

    public NasdaqIrInsightScraperWorker(
        ILogger<NasdaqIrInsightScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<NasdaqIrInsightScraperOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override Task DoWork(CancellationToken stoppingToken) =>
        RunImport<NasdaqIrInsightScraperService>(stoppingToken);
}
