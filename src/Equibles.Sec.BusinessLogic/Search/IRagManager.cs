using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;

namespace Equibles.Sec.BusinessLogic.Search;

public interface IRagManager
{
    public Task<List<Chunk>> SearchRelevantChunks(
        string query,
        int maxResults = 5,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        IReadOnlyCollection<string> excludeTickers = null,
        int maxResultsPerCompany = 0,
        bool broadenSparseResults = false,
        SearchMatch match = SearchMatch.Ranked
    );
    public Task<List<Chunk>> SearchRelevantChunksByCompany(
        string query,
        string ticker,
        int maxResults = 5,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool broadenSparseResults = false,
        SearchMatch match = SearchMatch.Ranked
    );
    public Task<List<Chunk>> SearchRelevantChunksByDocumentType(
        string query,
        DocumentType documentType,
        int maxResults = 5
    );
    public Task<List<Chunk>> SearchRelevantChunksByDocument(
        string query,
        Guid documentId,
        int maxResults = 5,
        bool broadenSparseResults = false
    );

    // The Detailed variants also report whether a keyword pass ran out of budget, so a caller can
    // tell "only N matched" from "the search stopped after finding N".
    public Task<ChunkSearchOutcome> SearchRelevantChunksDetailed(
        string query,
        int maxResults = 5,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        IReadOnlyCollection<string> excludeTickers = null,
        int maxResultsPerCompany = 0,
        bool broadenSparseResults = false,
        SearchMatch match = SearchMatch.Ranked
    );
    public Task<ChunkSearchOutcome> SearchRelevantChunksByCompanyDetailed(
        string query,
        string ticker,
        int maxResults = 5,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool broadenSparseResults = false,
        SearchMatch match = SearchMatch.Ranked
    );
    public Task<ChunkSearchOutcome> SearchRelevantChunksByDocumentDetailed(
        string query,
        Guid documentId,
        int maxResults = 5,
        bool broadenSparseResults = false
    );
    public Task<string> BuildContext(
        List<Chunk> chunks,
        bool includeDocumentIds = false,
        int maxExcerptChars = 0,
        bool includeExcerptLinks = false
    );
}
