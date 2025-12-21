using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.Sec.BusinessLogic.Processing;

public interface IDocumentProcessor {
    public Task ProcessDocuments(List<Document> documents, CancellationToken cancellationToken = default);
    public Task GenerateEmbeddings(List<Chunk> chunks, CancellationToken cancellationToken = default);
}
