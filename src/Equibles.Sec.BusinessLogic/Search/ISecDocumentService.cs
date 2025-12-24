using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.BusinessLogic.Search;

public interface ISecDocumentService {
    Task<List<SecDocumentInfo>> GetRecentDocuments(string ticker, DateTime? startDate = null, DateTime? endDate = null, int maxItems = 10, int page = 1, DocumentType documentType = null);
}
