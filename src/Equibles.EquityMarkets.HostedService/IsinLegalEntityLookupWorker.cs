using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;

namespace Equibles.EquityMarkets.HostedService;

// Fills GLEIF evidence for listed ISINs in bounded batches, continuing at once while a full batch was due.
public class IsinLegalEntityLookupWorker : BaseScraperWorker
{
    protected override string WorkerName => "GLEIF ISIN lookup";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(1);
    protected override ErrorSource ErrorSource => ErrorSource.EquityMarketsScraper;

    public IsinLegalEntityLookupWorker(
        ILogger<IsinLegalEntityLookupWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<IsinLegalEntityLookupImporter>();
        if (await importer.Refresh(stoppingToken) == IsinLegalEntityLookupImporter.BatchSize)
            RequestImmediateContinuation();
    }
}
