using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Equibles.Sec.FinancialFacts.HostedService;

/// <summary>
/// Captures the as-reported statement bundle (FilingSummary.xml + the statement R-files) from SEC
/// EDGAR for filings that already have a captured XBRL envelope. EDGAR-bound, so it processes one
/// batch per cycle and yields the shared SEC budget between bursts; per-document failures are
/// attempt-capped so a permanently-unfetchable filing can't starve the queue. The local parse
/// sweep turns the captured bundles into <see cref="Equibles.Sec.FinancialFacts.Data.Models.ReportedFinancialStatement"/>
/// rows.
/// </summary>
public class ReportedStatementsCaptureWorker : BaseScraperWorker
{
    private readonly ReportedStatementsCaptureOptions _options;
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "As-reported statements capture";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;

    // Start shortly after the other SEC sweeps have booted so they claim their initial watermark,
    // then this bulk capture drains aggressively alongside them.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(1);

    public ReportedStatementsCaptureWorker(
        ILogger<ReportedStatementsCaptureWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<ReportedStatementsCaptureOptions> options,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _options = options.Value;
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "As-reported statements capture",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        var batchSize = Math.Max(1, _options.BatchSize);

        await using var scope = ScopeFactory.CreateAsyncScope();
        var documentRepository = scope.ServiceProvider.GetRequiredService<DocumentRepository>();
        var captureService =
            scope.ServiceProvider.GetRequiredService<ReportedStatementsCaptureService>();

        // Only XBRL filings have a FilingSummary / R-files; newest first so fresh quarters get
        // their as-reported statements soonest while the historical drain catches up behind them.
        var batch = await documentRepository
            .GetByXbrlStatus(XbrlCaptureStatus.Captured)
            .Where(d =>
                d.ReportedStatementsStatus == XbrlCaptureStatus.NotChecked
                && d.ReportedStatementsCaptureAttempts
                    < Document.MaxReportedStatementsCaptureAttempts
            )
            .OrderByDescending(d => d.ReportingDate)
            .Take(batchSize)
            .ToListAsync(stoppingToken);

        if (batch.Count == 0)
        {
            return;
        }

        var captured = 0;
        foreach (var document in batch)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                document.ReportedStatementsStatus = await captureService.Capture(
                    document,
                    _options.MaxParallelFetches,
                    stoppingToken
                );
                if (document.ReportedStatementsStatus == XbrlCaptureStatus.Captured)
                {
                    captured++;
                }
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
                document.ReportedStatementsCaptureAttempts++;
                Logger.LogWarning(
                    ex,
                    "As-reported statements capture failed for document {DocumentId} ({Accession}); will retry.",
                    document.Id,
                    document.AccessionNumber
                );
                await ErrorReporter.Report(
                    ErrorSource,
                    "ReportedStatementsCapture.Capture",
                    ex.Message,
                    ex.StackTrace,
                    $"documentId: {document.Id}, accession: {document.AccessionNumber}"
                );
            }
            await documentRepository.SaveChanges();
        }

        Logger.LogInformation(
            "As-reported statements capture cycle: {Documents} document(s) processed, {Captured} captured.",
            batch.Count,
            captured
        );

        // A full batch means more backlog — drain in bursts (a brief continuation) so a large
        // backfill proceeds while still yielding the shared EDGAR budget between cycles.
        if (batch.Count == batchSize)
        {
            RequestImmediateContinuation();
        }
    }
}
