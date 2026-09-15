using Equibles.EquityMarkets.BusinessLogic.Firds;
using Equibles.EquityMarkets.HostedService.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;

namespace Equibles.EquityMarkets.HostedService;

public class FirdsUniverseWorker : BaseScraperWorker
{
    protected override string WorkerName => "FIRDS universe scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.EquityMarketsScraper;

    public FirdsUniverseWorker(
        ILogger<FirdsUniverseWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<EquityMarketsScraperOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(Math.Max(1, options.Value.SleepIntervalHours));
    }

    protected override Task DoWork(CancellationToken stoppingToken) =>
        RunImport<FirdsUniverseImporter>(stoppingToken);
}
