using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.FinancialFacts.HostedService;

/// <summary>
/// Sweeps documents with a captured raw XBRL envelope and extracts their
/// dimensional financial facts (see <see cref="XbrlFactExtractionService"/>).
/// Selection is version-stamped: a document qualifies while its
/// <c>XbrlFactsVersion</c> is below <see cref="XbrlFactExtractionService.CurrentVersion"/>,
/// so bumping the extractor version reprocesses the corpus. Per-document
/// failures bump <c>XbrlFactsAttempts</c> and are retried until the ceiling
/// (a persistently-failing backlog can burn several attempts within one
/// drain), so one unparseable envelope can't starve the queue. Purely local
/// work (stored envelopes, no EDGAR requests).
/// </summary>
public class XbrlFactsExtractionWorker : BaseScraperWorker
{
    // After this many failed cycles a document is no longer selected, mirroring
    // the capture backfill's ceiling.
    private const int MaxAttempts = 5;

    private readonly XbrlFactsExtractionOptions _options;

    protected override string WorkerName => "XBRL facts extraction";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;

    // Let the live ingest/backfill land fresh captures first after a deploy;
    // this sweep has no external budget to compete for.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(5);

    public XbrlFactsExtractionWorker(
        ILogger<XbrlFactsExtractionWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<XbrlFactsExtractionOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _options = options.Value;
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        var batchSize = Math.Max(1, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var documentRepository = scope.ServiceProvider.GetRequiredService<DocumentRepository>();
            var extractionService =
                scope.ServiceProvider.GetRequiredService<XbrlFactExtractionService>();

            // Newest filings first so fresh quarters get their dimensional
            // rows soonest while the historical drain catches up behind them.
            var batch = await documentRepository
                .GetByXbrlStatus(XbrlCaptureStatus.Captured)
                .Where(d =>
                    d.XbrlFactsVersion < XbrlFactExtractionService.CurrentVersion
                    && d.XbrlFactsAttempts < MaxAttempts
                )
                .OrderByDescending(d => d.ReportingDate)
                .Take(batchSize)
                .ToListAsync(stoppingToken);

            if (batch.Count == 0)
                return;

            var extracted = 0;
            foreach (var document in batch)
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    extracted += await extractionService.Extract(document, stoppingToken);
                    // A clean parse with zero dimensional facts is terminal too —
                    // the envelope simply carries none worth re-reading.
                    document.XbrlFactsVersion = XbrlFactExtractionService.CurrentVersion;
                }
                // Shutdown mid-batch is not a document failure: let it surface
                // so the base loop winds down quietly instead of burning one of
                // the document's attempts and landing a phantom row in Errors.
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                // Per-document fault isolation: count the attempt and keep the
                // cycle going for the rest of the batch.
                catch (Exception ex)
                {
                    document.XbrlFactsAttempts++;
                    Logger.LogWarning(
                        ex,
                        "Dimensional-fact extraction failed for document {DocumentId} ({Accession}); will retry.",
                        document.Id,
                        document.AccessionNumber
                    );
                    await ErrorReporter.Report(
                        ErrorSource,
                        "XbrlFactsExtraction.Extract",
                        ex,
                        $"documentId: {document.Id}, accession: {document.AccessionNumber}"
                    );
                }
                // Flushes this scope's change tracker, which must only ever hold
                // the Document batch (plus lazily-loaded navigations): the
                // extraction service confines all fact/dimension writes to its
                // own scoped contexts, so this save persists exactly the
                // version/attempt stamps. Keep it that way.
                await documentRepository.SaveChanges();
            }

            Logger.LogInformation(
                "XBRL facts extraction cycle: {Documents} document(s) processed, {Facts} dimensional fact(s) persisted.",
                batch.Count,
                extracted
            );

            // A partial batch means the queue is drained; a full one means
            // there is more backlog — keep draining within this cycle.
            if (batch.Count < batchSize)
                return;
        }
    }
}
