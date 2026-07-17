using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.BusinessLogic.Search;

public interface ISecDocumentService
{
    Task<List<SecDocumentInfo>> GetRecentDocuments(
        string ticker,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int maxItems = 10,
        int page = 1,
        DocumentType documentType = null
    );

    /// <summary>
    /// Counts the documents <see cref="GetRecentDocuments"/> would page through for the same
    /// filters — including the hidden-type exclusion applied when no explicit type is requested —
    /// so callers can report totals and page counts alongside a page of results.
    /// </summary>
    Task<int> CountDocuments(
        string ticker,
        DateTime? startDate = null,
        DateTime? endDate = null,
        DocumentType documentType = null
    );
}
