using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.GovernmentContracts.HostedService.Configuration;
using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Options;

namespace Equibles.GovernmentContracts.HostedService;

public class GovernmentContractsScraperWorker : BaseScraperWorker
{
    protected override string WorkerName => "Government contracts scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.GovernmentContractsScraper;

    public GovernmentContractsScraperWorker(
        ILogger<GovernmentContractsScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<GovernmentContractsScraperOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override Task DoWork(CancellationToken stoppingToken) =>
        RunImport<GovernmentContractsImportService>(stoppingToken);
}
