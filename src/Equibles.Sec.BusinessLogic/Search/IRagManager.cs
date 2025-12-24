using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.Sec.BusinessLogic.Search;

public interface IRagManager {
    public Task<List<Chunk>> SearchRelevantChunks(string query, int maxResults = 5, DocumentType documentType = null, DateOnly? startDate = null, DateOnly? endDate = null);
    public Task<List<Chunk>> SearchRelevantChunksByCompany(string query, string ticker, int maxResults = 5, DocumentType documentType = null, DateOnly? startDate = null, DateOnly? endDate = null);
    public Task<List<Chunk>> SearchRelevantChunksByDocumentType(string query, DocumentType documentType, int maxResults = 5);
    public Task<List<Chunk>> SearchRelevantChunksByDocument(string query, Guid documentId, int maxResults = 5);
    public Task<string> BuildContext(List<Chunk> chunks);
}
