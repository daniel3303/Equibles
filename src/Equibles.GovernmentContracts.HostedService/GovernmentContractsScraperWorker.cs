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

    // USAspending has documented bad spells lasting minutes (see UsaSpendingClient's
    // retry rationale); a transport failure that survives the client's ~2min of
    // retries usually clears within a backoff cycle or two. Only a streak of three
    // faulted cycles (~1+2+4min of backoff) is worth an Errors-page row — the same
    // brief-blip reasoning as DocumentProcessorWorker's threshold.
    protected override int ErrorReportThreshold => 3;

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
