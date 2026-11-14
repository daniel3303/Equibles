using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.FinancialFacts.HostedService;

/// <summary>
/// Runs the dimension-set backfill daily until the per-fact dimension table is retired. A pass
/// with nothing left only reads, and restarts skip a fresh cycle.
/// </summary>
public class FinancialDimensionSetBackfillWorker(
    ILogger<FinancialDimensionSetBackfillWorker> logger,
    IServiceScopeFactory scopeFactory,
    ErrorReporter errorReporter
) : BaseScraperWorker(logger, scopeFactory, errorReporter)
{
    protected override string WorkerName => "Dimension set backfill";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;
    protected override bool SkipStartupCycleWhenFresh => true;

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var backfill = scope.ServiceProvider.GetRequiredService<FinancialDimensionSetBackfill>();
        var pass = await backfill.Run(stoppingToken);
        Logger.LogInformation(
            "{Worker} added {Added} dimension sets; {Unresolved} keys have no fact that states them in full",
            WorkerName,
            pass.Added,
            pass.Unresolved
        );
    }
}
