using Equibles.Data;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Helpers;

/// <summary>
/// Builds a <see cref="HybridChunkSearcher"/> wired for tests that assert the real ParadeDB BM25
/// ranking without an embedding server. The semantic arm is disabled, so the searcher degrades to
/// exactly the keyword-only path those tests were written against.
/// </summary>
public static class HybridChunkSearcherFactory
{
    public static HybridChunkSearcher Bm25Only(EquiblesFinancialDbContext dbContext)
    {
        return new HybridChunkSearcher(
            new ChunkRepository(dbContext),
            new EmbeddingRepository(dbContext),
            embeddingClient: null,
            Options.Create(new HybridSearchOptions { Enabled = false }),
            Options.Create(new EmbeddingConfig()),
            NullLogger<HybridChunkSearcher>.Instance
        );
    }
}
