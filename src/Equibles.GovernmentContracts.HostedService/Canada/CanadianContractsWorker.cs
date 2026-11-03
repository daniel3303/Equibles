using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.GovernmentContracts.HostedService.Canada;

/// <summary>Reads the Government of Canada's proactive publication of contracts twice a day, draining the backlog of older quarters cycle after cycle.</summary>
public class CanadianContractsWorker : BaseScraperWorker
{
    protected override string WorkerName => "Canadian government contracts";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(12);
    protected override ErrorSource ErrorSource => ErrorSource.GovernmentContractsScraper;

    // A refused or failed page ends the cycle and the next one reads the quarter again; only a streak is worth an error.
    protected override int ErrorReportThreshold => 3;

    public CanadianContractsWorker(
        ILogger<CanadianContractsWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var import = scope.ServiceProvider.GetRequiredService<CanadianContractsImportService>();
        await import.Import(stoppingToken);
        if (import.BacklogRemains)
            RequestImmediateContinuation();
    }
}
