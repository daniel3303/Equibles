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

        // Only documents that came from a filing (an accession to re-fetch) qualify; legacy
        // and paper-only rows have no accession. Newest first so recent filings fill in soonest.
        var query = _documentRepository
            .GetByXbrlStatus(XbrlCaptureStatus.NotChecked)
            .Where(d => d.AccessionNumber != null);

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

            try
            {
                var cik = document.CommonStock?.Cik;
                if (string.IsNullOrEmpty(cik))
                {
                    // Without a CIK the submission can't be located; leave NotChecked.
                    result.Skipped++;
                    continue;
                }

                var content = await _secEdgarClient.GetDocumentContent(
                    document.AccessionNumber,
                    cik
                );
                var capture = _captureService.Capture(
                    content,
                    new FilingData { Cik = cik, AccessionNumber = document.AccessionNumber }
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
                        result.Skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                // Best-effort: leave the document NotChecked so a later cycle retries it.
                result.Failed++;
                _logger.LogWarning(
                    ex,
                    "XBRL backfill failed for document {DocumentId} ({Accession}); will retry.",
                    document.Id,
                    document.AccessionNumber
                );
            }
        }

        return result;
    }
}
