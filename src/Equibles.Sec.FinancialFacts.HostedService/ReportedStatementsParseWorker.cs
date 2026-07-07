using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.HostedService;

/// <summary>
/// Reconstructs as-reported statements from already-captured R-file bundles. Purely local work
/// (no EDGAR), version-stamped: a document qualifies while its
/// <see cref="Document.ReportedStatementsParseVersion"/> is below
/// <see cref="Document.ReportedStatementsParserVersion"/>, so bumping the parser re-derives the
/// corpus. Per-document failures are attempt-capped so one unparseable bundle can't starve the queue.
/// </summary>
public class ReportedStatementsParseWorker : BaseScraperWorker
{
    private readonly ReportedStatementsParseOptions _options;

    protected override string WorkerName => "As-reported statements parse";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;

    // Let live capture land bundles first after a deploy; this sweep has no external budget.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(12);

    public ReportedStatementsParseWorker(
        ILogger<ReportedStatementsParseWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<ReportedStatementsParseOptions> options
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
            var parseService =
                scope.ServiceProvider.GetRequiredService<ReportedStatementsParseService>();

            // Captured bundles whose parse is below the current parser version, newest first.
            var batch = await documentRepository
                .GetByReportedStatementsStatus(XbrlCaptureStatus.Captured)
                .Where(d =>
                    d.ReportedStatementsParseVersion < Document.ReportedStatementsParserVersion
                    && d.ReportedStatementsParseAttempts
                        < Document.MaxReportedStatementsParseAttempts
                )
                .OrderByDescending(d => d.ReportingDate)
                .Take(batchSize)
                .ToListAsync(stoppingToken);

            if (batch.Count == 0)
            {
                return;
            }

            var statements = 0;
            foreach (var document in batch)
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    statements += await parseService.Parse(document, stoppingToken);
                    document.ReportedStatementsParseVersion =
                        Document.ReportedStatementsParserVersion;
                }
                // Shutdown mid-batch is not a document failure: let it surface so the
                // base loop winds down quietly instead of burning one of the document's
                // attempts and landing a phantom row in Errors.
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                // Per-document fault isolation: count the attempt and keep the cycle going.
                catch (Exception ex)
                {
                    document.ReportedStatementsParseAttempts++;
                    Logger.LogWarning(
                        ex,
                        "As-reported statements parse failed for document {DocumentId} ({Accession}); will retry.",
                        document.Id,
                        document.AccessionNumber
                    );
                    await ErrorReporter.Report(
                        ErrorSource,
                        "ReportedStatementsParse.Parse",
                        ex.Message,
                        ex.StackTrace,
                        $"documentId: {document.Id}, accession: {document.AccessionNumber}"
                    );
                }
                await documentRepository.SaveChanges();
            }

            Logger.LogInformation(
                "As-reported statements parse cycle: {Documents} document(s) processed, {Statements} statement(s) reconstructed.",
                batch.Count,
                statements
            );

            // A partial batch means the queue is drained; a full one means more backlog — keep
            // draining within this cycle (local work, no shared budget to yield).
            if (batch.Count < batchSize)
            {
                return;
            }
        }
    }
}
