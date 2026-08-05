using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Continuously brings insider transactions up to the current parser version.
/// Each cycle drains every filing whose rows sit below
/// <see cref="Equibles.InsiderTrading.Data.Models.InsiderTransaction.CurrentParserVersion"/>
/// — re-deriving security kind, price validity, and footnotes from the cached
/// ownership XML, fetching and caching that XML from EDGAR the first time a filing
/// is seen. The work is version-driven and resumable, so it survives restarts and
/// automatically re-enrolls every filing after a parser-version bump — no manual
/// trigger needed.
///
/// Runs in the worker process so it shares the single SEC rate-limiter with the
/// other EDGAR scrapers (rather than competing as a separate process), and starts
/// after a stagger so it doesn't contend for the request budget at deploy time.
/// </summary>
public class InsiderFilingReprocessWorker : BaseScraperWorker
{
    protected override string WorkerName => "Insider filing reprocess";
    protected override ErrorSource ErrorSource => ErrorSource.InsiderTradingReprocess;

    // Once the backlog is drained each cycle finds nothing and idles; a periodic
    // re-check is only meaningful to pick up rows left pending after a transient
    // failure or a future parser-version bump.
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);

    // Stagger past deploy so the initial EDGAR burst doesn't collide with the
    // other SEC scrapers starting at the same time.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(5);

    public InsiderFilingReprocessWorker(
        ILogger<InsiderFilingReprocessWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    // One-shot marker for the sharesless-verdict reopen: unlike the signature restore, that
    // pass is NOT self-terminating (a re-flagged row matches its predicate again), so it runs
    // exactly once and the marker keeps it off every later cycle.
    private const string ShareslessReopenMarker = "InsiderTrading.ShareslessVerdictReopen";

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        // Restore rows the old basis-blind validator misrepaired BEFORE the version-driven
        // reprocess, so the nulled rows join the pending population this same cycle drains.
        // Bounded and self-terminating; a no-op scan once the back catalogue is clean.
        // Isolated failure domain: a sweep fault (timeout, deploy-time DB bounce) must not
        // starve the reprocess and backfill stages below, which do not depend on it.
        try
        {
            await using var sweepScope = ScopeFactory.CreateAsyncScope();
            var sweep =
                sweepScope.ServiceProvider.GetRequiredService<InsiderMisrepairedPriceSweep>();
            await sweep.Run(stoppingToken);

            var backfillStateRepo =
                sweepScope.ServiceProvider.GetRequiredService<BackfillStateRepository>();
            if (await backfillStateRepo.GetByName(ShareslessReopenMarker) == null)
            {
                var reopened = await sweep.ReopenShareslessVerdicts(stoppingToken);
                if (reopened < InsiderMisrepairedPriceSweep.MaxRowsPerCycle)
                {
                    // Drained in this pass — stamp the marker so the non-self-terminating
                    // predicate never re-opens rows the fixed validator re-flagged.
                    backfillStateRepo.Add(
                        new BackfillState
                        {
                            Name = ShareslessReopenMarker,
                            LastFullRescanAt = DateTime.UtcNow,
                        }
                    );
                    await backfillStateRepo.SaveChanges();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Misrepaired-price sweep failed; continuing with reprocess");
        }

        await using var scope = ScopeFactory.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<InsiderFilingReprocessManager>();

        var result = await manager.Run(cancellationToken: stoppingToken);

        if (result.Processed > 0)
            Logger.LogInformation("Insider filing reprocess cycle: {Summary}", result.Summary);

        // Drain the pending (IsPriceValid = null) population — freshly restored rows above,
        // plus rows whose close has since landed — through the same evaluation the backoffice
        // recompute uses. DB-only work; no EDGAR budget consumed.
        await using (var backfillScope = ScopeFactory.CreateAsyncScope())
        {
            var backfill =
                backfillScope.ServiceProvider.GetRequiredService<InsiderTransactionPriceBackfillManager>();
            var backfillResult = await backfill.Run(cancellationToken: stoppingToken);
            if (backfillResult.Processed > 0)
                Logger.LogInformation(
                    "Insider price backfill cycle: processed {Processed}, repaired={Repaired}, invalid={Invalid}, pending={Pending}",
                    backfillResult.Processed,
                    backfillResult.Repaired,
                    backfillResult.Invalid,
                    backfillResult.Pending
                );
        }
    }
}
