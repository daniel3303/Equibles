using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Services;

public interface IDocumentPersistenceService {
    Task<bool> Exists(CommonStock company, DocumentType documentType, DateOnly reportingDate, DateOnly reportingForDate);

    Task Save(CommonStock company, byte[] content, string fileName, DocumentType documentType,
        DateOnly reportingDate, DateOnly reportingForDate, string sourceUrl, CancellationToken cancellationToken = default);
}
