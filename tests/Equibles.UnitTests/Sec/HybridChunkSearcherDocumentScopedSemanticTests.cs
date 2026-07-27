using Equibles.Data;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

// Document-scoped searches must use the exhaustive in-document vector ranking regardless of
// the configured VectorSource: one document's chunks are a bounded set served by btree
// indexes, so no ANN index is needed, and it is the only way a purely semantic query (zero
// token overlap with the filing's wording) can find its passage — the pool re-rank can, by
// construction, never surface a chunk BM25 didn't retrieve. Corpus-WIDE searches without an
// ANN index must NOT take that path: an unscoped nearest-neighbour query sequential-scans the
// whole Embedding table (122 GB in production), so under Pool mode an empty BM25 pool stays
// an empty result until the corpus index exists.
public class HybridChunkSearcherDocumentScopedSemanticTests
{
    [Fact]
    public async Task DocumentScoped_PoolMode_EmptyBm25_ReturnsSemanticallyRankedChunks()
    {
        var documentId = Guid.NewGuid();
        var chunk = new Chunk
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Content = "management discussed operational headwinds",
        };
        var chunkRepository = new StubChunkRepository(bm25Results: [], allChunks: [chunk]);
        var embeddingRepository = new StubEmbeddingRepository(similarChunkIds: [chunk.Id]);
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search(
            "what challenges did leadership acknowledge",
            5,
            documentId: documentId
        );

        Assert.Single(results);
        Assert.Equal(chunk.Id, results[0].Id);
        Assert.True(embeddingRepository.SearchSimilarChunksCalled);
    }

    [Fact]
    public async Task CorpusWide_PoolMode_EmptyBm25_ReturnsEmptyWithoutTheVectorArm()
    {
        var chunkRepository = new StubChunkRepository(bm25Results: [], allChunks: []);
        var embeddingRepository = new StubEmbeddingRepository(similarChunkIds: []);
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search("some query no token matches", 5);

        Assert.Empty(results);
        Assert.False(embeddingRepository.SearchSimilarChunksCalled);
    }

    private static HybridChunkSearcher NewSearcher(
        ChunkRepository chunkRepository,
        EmbeddingRepository embeddingRepository
    )
    {
        var embeddingClient = Substitute.For<IEmbeddingClient>();
        embeddingClient.IsEnabled.Returns(true);
        embeddingClient
            .GenerateEmbedding(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.1f, 0.2f }));

        return new HybridChunkSearcher(
            chunkRepository,
            embeddingRepository,
            embeddingClient,
            // Explicit Pool: these tests pin the POOL-mode contract; the default is Auto,
            // whose scope-dependent behaviour has its own test class.
            Options.Create(new HybridSearchOptions { VectorSource = VectorSource.Pool }),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            NullLogger<HybridChunkSearcher>.Instance
        );
    }
}
