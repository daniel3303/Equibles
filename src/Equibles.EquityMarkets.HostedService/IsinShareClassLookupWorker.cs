using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;

namespace Equibles.EquityMarkets.HostedService;

// Fills OpenFIGI share class evidence for listed ISINs in bounded batches, continuing at once while a full batch was due.
public class IsinShareClassLookupWorker : BaseScraperWorker
{
    protected override string WorkerName => "OpenFIGI share class lookup";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(1);
    protected override ErrorSource ErrorSource => ErrorSource.EquityMarketsScraper;

    public IsinShareClassLookupWorker(
        ILogger<IsinShareClassLookupWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<IsinShareClassLookupImporter>();
        if (await importer.Refresh(stoppingToken) == IsinShareClassLookupImporter.BatchSize)
            RequestImmediateContinuation();
    }
}
