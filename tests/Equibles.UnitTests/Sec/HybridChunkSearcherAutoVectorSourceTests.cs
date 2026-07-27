using Equibles.Data;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

// Auto is the default VectorSource: a TICKER- or document-scoped search uses the exhaustive
// vector ranking over its bounded scope (a company's chunks are reached through the Chunk
// ticker btree index — no ANN index needed), so a purely semantic query can surface chunks
// BM25 never retrieved. An UNSCOPED search must NOT take that path — corpus-wide nearest
// neighbours without an ANN index distance-sort the whole Embedding table (85s measured on
// the production corpus) — it keeps the pool re-rank, which on an empty BM25 pool returns
// empty exactly as Pool mode does.
public class HybridChunkSearcherAutoVectorSourceTests
{
    [Fact]
    public async Task TickerScoped_AutoMode_EmptyBm25_ReturnsSemanticallyRankedChunks()
    {
        var chunk = new Chunk
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Content = "management discussed operational headwinds",
        };
        var chunkRepository = new StubChunkRepository(bm25Results: [], allChunks: [chunk]);
        var embeddingRepository = new StubEmbeddingRepository(similarChunkIds: [chunk.Id]);
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search(
            "what challenges did leadership acknowledge",
            5,
            ticker: "AAPL"
        );

        Assert.Single(results);
        Assert.Equal(chunk.Id, results[0].Id);
        Assert.True(embeddingRepository.SearchSimilarChunksCalled);
        Assert.Equal("AAPL", embeddingRepository.SearchSimilarChunksTicker);
    }

    [Fact]
    public async Task Unscoped_AutoMode_EmptyBm25_ReturnsEmptyWithoutTheCorpusArm()
    {
        var chunkRepository = new StubChunkRepository(bm25Results: [], allChunks: []);
        var embeddingRepository = new StubEmbeddingRepository(similarChunkIds: []);
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search("some query no token matches", 5);

        Assert.Empty(results);
        Assert.False(embeddingRepository.SearchSimilarChunksCalled);
    }

    [Fact]
    public async Task TickerScoped_ExplicitPoolMode_NeverTakesTheCorpusArm()
    {
        var chunkRepository = new StubChunkRepository(bm25Results: [], allChunks: []);
        var embeddingRepository = new StubEmbeddingRepository(similarChunkIds: []);
        var searcher = NewSearcher(chunkRepository, embeddingRepository, VectorSource.Pool);

        var results = await searcher.Search("some query no token matches", 5, ticker: "AAPL");

        Assert.Empty(results);
        Assert.False(embeddingRepository.SearchSimilarChunksCalled);
    }

    [Fact]
    public void Auto_IsTheDefaultVectorSource()
    {
        Assert.Equal(VectorSource.Auto, new HybridSearchOptions().VectorSource);
    }

    private static HybridChunkSearcher NewSearcher(
        ChunkRepository chunkRepository,
        EmbeddingRepository embeddingRepository,
        VectorSource vectorSource = VectorSource.Auto
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
            Options.Create(new HybridSearchOptions { VectorSource = vectorSource }),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            NullLogger<HybridChunkSearcher>.Instance
        );
    }
}
