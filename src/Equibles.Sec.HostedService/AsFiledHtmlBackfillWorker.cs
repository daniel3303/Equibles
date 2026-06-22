using Equibles.Core.Configuration;
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
/// Builds the stitched as-filed HTML for 8-K documents ingested before the as-filed view
/// existed (or stitched by an older builder version). Runs alongside the live scraper (sharing
/// the EDGAR request budget) and is gated on both the master capture switch and the dedicated
/// backfill switch, so the historical sweep only runs when an operator opts in.
/// </summary>
public class AsFiledHtmlBackfillWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly AsFiledHtmlCaptureOptions _captureOptions;
    private readonly WorkerOptions _workerOptions;

    protected override string WorkerName => "As-filed HTML backfill";

    // A historical sweep, not latency-sensitive: a longer idle keeps it from re-spending the
    // shared EDGAR budget when the queue is drained or stuck on exhausted documents.
    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);

    // While a backlog is still draining (a cycle that filled its batch) the loop uses this
    // shorter interval so a large sweep clears in days rather than weeks.
    protected override TimeSpan ContinuationInterval =>
        TimeSpan.FromSeconds(_captureOptions.BackfillDrainIntervalSeconds);

    protected override ErrorSource ErrorSource => ErrorSource.DocumentScraper;

    // Yield to the live SEC scrapers at deploy time before spending the shared EDGAR budget.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(10);

    public AsFiledHtmlBackfillWorker(
        ILogger<AsFiledHtmlBackfillWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<AsFiledHtmlCaptureOptions> captureOptions,
        IOptions<WorkerOptions> workerOptions,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _captureOptions = captureOptions.Value;
        _workerOptions = workerOptions.Value;
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "As-filed HTML backfill",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        if (!_captureOptions.Enabled || !_captureOptions.BackfillEnabled)
        {
            Logger.LogDebug("As-filed HTML backfill disabled; skipping cycle.");
            return;
        }

        var minReportingDate = _workerOptions.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : (DateOnly?)null;

        var batchSize = _captureOptions.BackfillBatchSize;
        await using var scope = ScopeFactory.CreateAsyncScope();
        var backfillService =
            scope.ServiceProvider.GetRequiredService<AsFiledHtmlBackfillService>();
        var result = await backfillService.Backfill(batchSize, minReportingDate, stoppingToken);

        // A full batch that made forward progress means the newest-first queue still has
        // selectable documents, so drain the next batch after the short ContinuationInterval.
        var madeProgress = result.Failed < result.Processed;
        if (batchSize > 0 && result.Processed >= batchSize && madeProgress)
        {
            RequestImmediateContinuation();
        }

        Logger.LogInformation(
            "As-filed HTML backfill cycle complete. Processed: {Processed}, Built: {Built}, "
                + "NoExhibit: {NoExhibit}, Failed: {Failed}",
            result.Processed,
            result.Built,
            result.NoExhibit,
            result.Failed
        );
    }
}
