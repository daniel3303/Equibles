using System.Text.RegularExpressions;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Fills in the raw XBRL envelope for documents ingested before capture was enabled. Each
/// cycle takes a batch of <see cref="XbrlCaptureStatus.NotChecked"/> documents (newest first,
/// bounded by the configured minimum sync date), re-fetches the filing submission, runs the
/// same extraction as the live ingest path, and records the outcome. Per-document failures
/// are swallowed so the document stays <c>NotChecked</c> and is retried next cycle.
/// </summary>
public class XbrlBackfillService
{
    // After this many failed cycles a document is no longer selected, so a permanently
    // unfetchable filing (deleted/superseded on EDGAR, unparseable) can't sit at the head of
    // the newest-first queue and starve every older document behind it.
    private const int MaxAttempts = 5;

    // Rows ingested before AccessionNumber existed carry it only inside the stored EDGAR
    // full-submission URL (https://www.sec.gov/Archives/edgar/data/{cik}/{accession}.txt).
    // The accession is the file name; recovering it makes those rows backfillable.
    private static readonly Regex EdgarSourceUrlAccession = new(
        @"/Archives/edgar/data/\d+/(\d{10}-\d{2}-\d{6})\.txt$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private readonly DocumentRepository _documentRepository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly XbrlEnvelopeCaptureService _captureService;
    private readonly IDocumentPersistenceService _persistenceService;
    private readonly ILogger<XbrlBackfillService> _logger;

    public XbrlBackfillService(
        DocumentRepository documentRepository,
        ISecEdgarClient secEdgarClient,
        XbrlEnvelopeCaptureService captureService,
        IDocumentPersistenceService persistenceService,
        ILogger<XbrlBackfillService> logger
    )
    {
        _documentRepository = documentRepository;
        _secEdgarClient = secEdgarClient;
        _captureService = captureService;
        _persistenceService = persistenceService;
        _logger = logger;
    }

    public async Task<XbrlBackfillResult> Backfill(
        int batchSize,
        DateOnly? minReportingDate,
        CancellationToken cancellationToken = default
    )
    {
        var result = new XbrlBackfillResult();
        if (batchSize <= 0)
        {
            return result;
        }

        // Only documents that came from a filing and whose issuer has a CIK qualify: either
        // they carry the accession directly, or it can be recovered from the stored EDGAR
        // submission URL (rows ingested before AccessionNumber existed). Non-EDGAR documents
        // (e.g. earnings-call transcripts) have neither and are never selected. Documents
        // that have exhausted their retry ceiling are dropped from the working set so they
        // can't block the queue. Newest first so recent filings fill in soonest.
        var query = _documentRepository
            .GetByXbrlStatus(XbrlCaptureStatus.NotChecked)
            .Where(d =>
                (
                    d.AccessionNumber != null
                    || (d.SourceUrl != null && d.SourceUrl.Contains("/Archives/edgar/data/"))
                )
                && d.CommonStock.Cik != null
                && d.XbrlCaptureAttempts < MaxAttempts
            );

        if (minReportingDate.HasValue)
        {
            query = query.Where(d => d.ReportingDate >= minReportingDate.Value);
        }

        var batch = await query
            .Include(d => d.CommonStock)
            .OrderByDescending(d => d.ReportingDate)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var document in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Processed++;
            // Count the attempt up front so a fetch/parse failure still advances the retry
            // ceiling and the document eventually drops out of the working set.
            document.XbrlCaptureAttempts++;

            // Recover the accession for legacy rows and persist it (UpdateXbrl /
            // PersistAttempt save it alongside the outcome) so later consumers can link the
            // filing without re-deriving. A URL that matched the broad SQL filter but not
            // the strict accession shape can never be fetched — record the failure and let
            // the attempt ceiling walk it out of the working set.
            document.AccessionNumber ??= DeriveAccessionNumber(document.SourceUrl);
            if (document.AccessionNumber == null)
            {
                result.Failed++;
                _logger.LogWarning(
                    "XBRL backfill cannot derive an accession number from SourceUrl {SourceUrl} for document {DocumentId}.",
                    document.SourceUrl,
                    document.Id
                );
                await PersistAttempt();
                continue;
            }

            try
            {
                var content = await _secEdgarClient.GetDocumentContent(
                    document.AccessionNumber,
                    document.CommonStock.Cik
                );
                var capture = _captureService.Capture(
                    content,
                    new FilingData
                    {
                        Cik = document.CommonStock.Cik,
                        AccessionNumber = document.AccessionNumber,
                    }
                );
                await _persistenceService.UpdateXbrl(document, capture);

                switch (capture.Status)
                {
                    case XbrlCaptureStatus.Captured:
                        result.Captured++;
                        break;
                    case XbrlCaptureStatus.NotPresent:
                        result.NotPresent++;
                        break;
                    default:
                        // Successful fetch but extraction left it NotChecked — persisted with
                        // the bumped attempt count by UpdateXbrl; retried until the ceiling.
                        result.Skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                // Best-effort: leave the document NotChecked so a later cycle retries it, but
                // persist the bumped attempt count so it can't be retried forever.
                result.Failed++;
                _logger.LogWarning(
                    ex,
                    "XBRL backfill failed for document {DocumentId} ({Accession}); will retry.",
                    document.Id,
                    document.AccessionNumber
                );
                await PersistAttempt();
            }
        }

        return result;
    }

    private static string DeriveAccessionNumber(string sourceUrl)
    {
        if (string.IsNullOrEmpty(sourceUrl))
        {
            return null;
        }

        var match = EdgarSourceUrlAccession.Match(sourceUrl);
        return match.Success ? match.Groups[1].Value : null;
    }

    // Saves the bumped attempt count after a fetch failure. Best-effort: a save failure here
    // must not abort the rest of the batch.
    private async Task PersistAttempt()
    {
        try
        {
            await _documentRepository.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist XBRL backfill attempt count.");
        }
    }
}
