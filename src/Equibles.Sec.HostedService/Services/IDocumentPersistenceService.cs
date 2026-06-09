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
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Applies a resolved XBRL capture result onto an already-persisted, tracked
    /// <see cref="Document"/> and saves — used by the backfill to fill in documents
    /// ingested before capture was enabled.
    /// </summary>
    Task UpdateXbrl(Document document, XbrlCaptureResult xbrl);
}
