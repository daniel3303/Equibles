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
/// Backfills the raw XBRL envelope for documents ingested before capture was enabled. Runs
/// alongside the live scraper (sharing the EDGAR request budget) and is gated on both the
/// master capture switch and the dedicated backfill switch, so the historical sweep only
/// runs when an operator opts in.
/// </summary>
public class XbrlBackfillWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly XbrlCaptureOptions _captureOptions;
    private readonly WorkerOptions _workerOptions;

    protected override string WorkerName => "XBRL backfill";

    // A historical sweep, not a latency-sensitive job: a longer interval keeps it from
    // re-querying and re-spending the shared EDGAR budget when the queue is drained or stuck
    // on exhausted documents.
    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);

    // While a backlog is still draining (a cycle that filled its batch) the loop uses this
    // shorter interval instead of SleepInterval, so a large historical sweep clears in days
    // rather than ~50 days of one batch every 5 minutes.
    protected override TimeSpan ContinuationInterval =>
        TimeSpan.FromSeconds(_captureOptions.BackfillDrainIntervalSeconds);

    protected override ErrorSource ErrorSource => ErrorSource.DocumentScraper;

    // Yield to the live SEC scrapers at deploy time before spending the shared EDGAR budget
    // on the historical sweep.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(10);

    public XbrlBackfillWorker(
        ILogger<XbrlBackfillWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<XbrlCaptureOptions> captureOptions,
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
        ValidateSecContactEmail(_configuration, "XBRL backfill", treatWhitespaceAsAbsent: true);

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        if (!_captureOptions.Enabled || !_captureOptions.BackfillEnabled)
        {
            Logger.LogDebug("XBRL backfill disabled; skipping cycle.");
            return;
        }

        var minReportingDate = _workerOptions.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : (DateOnly?)null;

        var batchSize = _captureOptions.BackfillBatchSize;
        await using var scope = ScopeFactory.CreateAsyncScope();
        var backfillService = scope.ServiceProvider.GetRequiredService<XbrlBackfillService>();
        var result = await backfillService.Backfill(batchSize, minReportingDate, stoppingToken);

        // A full batch that made forward progress means the newest-first queue still has
        // selectable documents, so drain the next batch after the short ContinuationInterval
        // instead of the full SleepInterval. Fall back to the 5-minute idle when the batch came
        // up short (backlog drained, or only retry-exhausted rows remain) OR when every document
        // failed — an all-failed full batch signals an EDGAR outage or an unfetchable head block,
        // exactly when bursting requests faster would hurt rather than help.
        var madeProgress = result.Failed < result.Processed;
        if (batchSize > 0 && result.Processed >= batchSize && madeProgress)
        {
            RequestImmediateContinuation();
        }

        Logger.LogInformation(
            "XBRL backfill cycle complete. Processed: {Processed}, Captured: {Captured}, "
                + "NotPresent: {NotPresent}, Skipped: {Skipped}, Failed: {Failed}",
            result.Processed,
            result.Captured,
            result.NotPresent,
            result.Skipped,
            result.Failed
        );
    }
}
