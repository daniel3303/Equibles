using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Backfills <c>Document.Items</c> for 8-Ks and 8-K/As ingested before item capture was
/// enabled. Runs alongside the live scraper (sharing the EDGAR request budget) and is gated
/// on an opt-in switch, so the historical sweep only runs when an operator asks for it.
/// </summary>
public class FilingItemsBackfillWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly FilingItemsBackfillOptions _options;

    // Set when a cycle finds no eligible company (the sweep is drained, or the switch is
    // off). The historical corpus is finite and every checked document gets a terminal
    // value, so once drained the 5-minute cadence only re-runs an empty scan of the 8-K
    // corpus — idle daily instead. A rare new pending row (a feed entry without items) is
    // picked up on the next daily tick or worker restart, and any non-empty cycle drops
    // straight back to the fast cadence.
    private bool _drained;

    protected override string WorkerName => "Filing items backfill";

    // A historical sweep, not a latency-sensitive job: a longer interval keeps it from
    // re-querying and re-spending the shared EDGAR budget when the queue is stuck on
    // companies whose feed cannot be fetched.
    protected override TimeSpan SleepInterval =>
        _drained ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(5);
    protected override ErrorSource ErrorSource => ErrorSource.DocumentScraper;

    // Yield to the live SEC scrapers at deploy time before spending the shared EDGAR budget
    // on the historical sweep.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(10);

    public FilingItemsBackfillWorker(
        ILogger<FilingItemsBackfillWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FilingItemsBackfillOptions> options,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _options = options.Value;
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "Filing items backfill",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            Logger.LogDebug("Filing-items backfill disabled; skipping cycle.");
            _drained = true;
            return;
        }

        await using var scope = ScopeFactory.CreateAsyncScope();
        var backfillService =
            scope.ServiceProvider.GetRequiredService<FilingItemsBackfillService>();
        var result = await backfillService.Backfill(_options.BatchSize, stoppingToken);

        Logger.LogInformation(
            "Filing-items backfill cycle complete. Companies: {Companies}, Stamped: {Stamped}, "
                + "NotFound: {NotFound}, Failed: {Failed}",
            result.Companies,
            result.Stamped,
            result.NotFound,
            result.Failed
        );

        var drainedNow = result.Companies == 0;
        if (drainedNow && !_drained)
        {
            Logger.LogInformation("Filing-items backfill drained; idling on the daily cadence.");
        }
        _drained = drainedNow;

        // A full batch means a backlog is still queued — burst through it instead of
        // spending a whole SleepInterval between batch-sized slices.
        if (!drainedNow && result.Companies >= _options.BatchSize)
        {
            RequestImmediateContinuation();
        }
    }
}
