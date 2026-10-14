using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Splits;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.FinancialFacts.HostedService;

/// <summary>
/// Judges captured splits against the issuers' cover-page share counts. Captures and cover pages
/// land through the day, and a pass is idempotent, so restarts skip a fresh cycle.
/// </summary>
public class SplitShareCountEvidenceWorker(
    ILogger<SplitShareCountEvidenceWorker> logger,
    IServiceScopeFactory scopeFactory,
    ErrorReporter errorReporter
) : BaseScraperWorker(logger, scopeFactory, errorReporter)
{
    protected override string WorkerName => "Split share-count evidence";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;
    protected override bool SkipStartupCycleWhenFresh => true;

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var reconciler =
            scope.ServiceProvider.GetRequiredService<SplitShareCountEvidenceReconciler>();
        var pass = await reconciler.Reconcile(stoppingToken);
        foreach (var (issuerId, error) in pass.Failures)
            await ErrorReporter.Report(
                ErrorSource,
                "SplitShareCountEvidence.Reconcile",
                error,
                $"issuer: {issuerId}"
            );
        Logger.LogInformation(
            "{Worker} changed the evidence on {Changed} split rows; {Failed} issuers failed",
            WorkerName,
            pass.Changed,
            pass.Failures.Count
        );
    }
}
