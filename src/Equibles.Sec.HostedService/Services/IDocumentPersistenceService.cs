using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;

namespace Equibles.Sec.HostedService.Services;

public interface IDocumentPersistenceService
{
    Task<bool> Exists(
        CommonStock company,
        DocumentType documentType,
        DateOnly reportingDate,
        DateOnly reportingForDate
    );

    Task Save(
        CommonStock company,
        byte[] content,
        string fileName,
        DocumentType documentType,
        DateOnly reportingDate,
        DateOnly reportingForDate,
        string sourceUrl,
        string accessionNumber = null,
        string items = null,
        XbrlCaptureResult xbrl = null,
        AsFiledHtmlCaptureResult asFiledHtml = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Applies a resolved XBRL capture result onto an already-persisted, tracked
    /// <see cref="Document"/> and saves — used by the backfill to fill in documents
    /// ingested before capture was enabled.
    /// </summary>
    Task UpdateXbrl(Document document, XbrlCaptureResult xbrl);

    /// <summary>
    /// Applies a resolved as-filed HTML build onto an already-persisted, tracked
    /// <see cref="Document"/> and saves — used by the backfill to stitch documents ingested
    /// before the as-filed view was built (or to re-stitch after a builder-version bump).
    /// </summary>
    Task UpdateAsFiledHtml(
        Document document,
        AsFiledHtmlCaptureResult asFiledHtml,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Replaces the body of an already-persisted <see cref="Document"/> in place, keeping its id —
    /// so soft references to it (e.g. an earnings call's TranscriptDocumentId) stay valid with no
    /// re-link — and dropping its stale chunks (their embeddings cascade) so the chunking worker
    /// re-chunks the new body on its next pass. Used when a document's source data is re-derived,
    /// e.g. an audio transcript regenerated after its speakers are re-resolved.
    /// </summary>
    Task ReplaceContent(
        Document document,
        byte[] content,
        CancellationToken cancellationToken = default
    );
}
