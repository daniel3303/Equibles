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
/// Backfills <c>Document.Items</c> for 8-Ks ingested before item capture was enabled. Runs
/// alongside the live scraper (sharing the EDGAR request budget) and is gated on an opt-in
/// switch, so the historical sweep only runs when an operator asks for it.
/// </summary>
public class FilingItemsBackfillWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly FilingItemsBackfillOptions _options;

    protected override string WorkerName => "Filing items backfill";

    // A historical sweep, not a latency-sensitive job: a longer interval keeps it from
    // re-querying and re-spending the shared EDGAR budget when the queue is drained or
    // stuck on companies whose feed cannot be fetched.
    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);
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
    }
}
