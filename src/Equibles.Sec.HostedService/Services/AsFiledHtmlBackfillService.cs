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
/// Builds the stitched as-filed HTML for 8-K documents ingested before the as-filed view
/// existed (or stitched by an older builder version). Each cycle takes a batch of pending 8-Ks
/// (newest first), re-fetches the filing submission, runs the same stitch as the live ingest
/// path, and stamps the builder version. Per-document failures are swallowed (the attempt count
/// advances) so one unbuildable filing can't starve the queue.
/// </summary>
public class AsFiledHtmlBackfillService
{
    // Rows ingested before AccessionNumber existed carry it only inside the stored EDGAR
    // full-submission URL; recovering it makes those rows backfillable.
    private static readonly Regex EdgarSourceUrlAccession = new(
        @"/Archives/edgar/data/\d+/(\d{10}-\d{2}-\d{6})\.txt$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private readonly DocumentRepository _documentRepository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly AsFiledHtmlCaptureService _captureService;
    private readonly IDocumentPersistenceService _persistenceService;
    private readonly ILogger<AsFiledHtmlBackfillService> _logger;

    public AsFiledHtmlBackfillService(
        DocumentRepository documentRepository,
        ISecEdgarClient secEdgarClient,
        AsFiledHtmlCaptureService captureService,
        IDocumentPersistenceService persistenceService,
        ILogger<AsFiledHtmlBackfillService> logger
    )
    {
        _documentRepository = documentRepository;
        _secEdgarClient = secEdgarClient;
        _captureService = captureService;
        _persistenceService = persistenceService;
        _logger = logger;
    }

    public async Task<AsFiledHtmlBackfillResult> Backfill(
        int batchSize,
        DateOnly? minReportingDate,
        CancellationToken cancellationToken = default
    )
    {
        var result = new AsFiledHtmlBackfillResult();
        if (batchSize <= 0)
        {
            return result;
        }

        var query = _documentRepository.GetPendingAsFiledHtml();

        if (minReportingDate.HasValue)
        {
            query = query.Where(d => d.ReportingDate >= minReportingDate.Value);
        }

        // Ids only: each document is loaded fresh inside the loop, so a failed capture's
        // half-applied change-tracker state can be discarded wholesale without detaching
        // the rest of the batch mid-flight.
        var batchIds = await query
            .OrderByDescending(d => d.ReportingDate)
            .Take(batchSize)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        foreach (var documentId in batchIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var document = await _documentRepository.Get(documentId);
            if (document == null)
                continue;

            result.Processed++;
            // Count the attempt up front so a fetch/stitch failure still advances the retry
            // ceiling and the document eventually drops out of the working set.
            document.AsFiledHtmlAttempts++;

            document.AccessionNumber ??= DeriveAccessionNumber(document.SourceUrl);
            if (document.AccessionNumber == null)
            {
                result.Failed++;
                _logger.LogWarning(
                    "As-filed HTML backfill cannot derive an accession number from SourceUrl {SourceUrl} for document {DocumentId}.",
                    document.SourceUrl,
                    document.Id
                );
                await RevertAndPersistAttempt(document, cancellationToken);
                continue;
            }

            try
            {
                var content = await _secEdgarClient.GetDocumentContent(
                    document.AccessionNumber,
                    document.CommonStock.Cik
                );
                // The submission doesn't name its primary document; TryBuildAsFiledHtml falls
                // back to the first displayable block (the primary by EDGAR convention).
                var capture = await _captureService.Capture(
                    content,
                    new FilingData
                    {
                        Cik = document.CommonStock.Cik,
                        AccessionNumber = document.AccessionNumber,
                    },
                    cancellationToken
                );
                await _persistenceService.UpdateAsFiledHtml(document, capture, cancellationToken);

                if (capture.Html != null)
                {
                    result.Built++;
                }
                else
                {
                    result.NoExhibit++;
                }
            }
            catch (Exception ex)
            {
                // Best-effort: leave the document below the builder version so a later cycle
                // retries it, but persist the bumped attempt count so it can't be retried forever.
                result.Failed++;
                _logger.LogWarning(
                    ex,
                    "As-filed HTML backfill failed for document {DocumentId} ({Accession}); will retry.",
                    document.Id,
                    document.AccessionNumber
                );
                await RevertAndPersistAttempt(document, cancellationToken);
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

    // Discards whatever the failed attempt left in the change tracker, then persists ONLY
    // the attempt bookkeeping. A failed UpdateAsFiledHtml can leave a half-applied capture
    // tracked (version stamp, added image/file rows) whose paired image-clear rolled back
    // with its transaction; the previous bare SaveChanges committed that mix and stamped
    // the document current with stale+new images, never to be retried. Best-effort: a
    // persistence failure here must not abort the rest of the batch.
    private async Task RevertAndPersistAttempt(
        Document document,
        CancellationToken cancellationToken
    )
    {
        _documentRepository.ClearChangeTracker();
        try
        {
            await _documentRepository.PersistAsFiledHtmlAttempt(document, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist as-filed HTML backfill attempt count.");
        }
    }
}
