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
    public async Task DocumentScoped_AutoMode_EmptyBm25_ReturnsSemanticallyRankedChunks()
    {
        // The shipped default must keep the document-scoped exhaustive ranking the Pool-mode
        // tests pin — a purely semantic in-document question stays answerable under Auto.
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
        Assert.True(embeddingRepository.SearchSimilarChunksCalled);
    }

    [Fact]
    public async Task TickerScoped_AutoMode_CorpusArmFailure_FallsBackToThePoolReRank()
    {
        // A failed corpus arm (missing index, slow distance sort) must not rank WORSE than the
        // old Pool default did: the pool re-rank over the BM25 candidates still runs.
        var rankedByVector = new Chunk
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Content = "second by keywords, first semantically",
        };
        var keywordOnly = new Chunk
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Content = "first by keywords, no stored vector",
        };
        var chunkRepository = new StubChunkRepository(
            bm25Results: [keywordOnly, rankedByVector],
            allChunks: [keywordOnly, rankedByVector]
        );
        var embeddingRepository = new StubEmbeddingRepository(
            similarChunkIds: [],
            throwOnCorpusSearch: true,
            storedEmbeddings:
            [
                new Embedding
                {
                    ChunkId = rankedByVector.Id,
                    Model = "test-model",
                    Vector = new Pgvector.Vector(new float[] { 0.1f, 0.2f }),
                },
            ]
        );
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search("semantic question", 5, ticker: "AAPL");

        Assert.True(embeddingRepository.SearchSimilarChunksCalled);
        Assert.Equal(2, results.Count);
        // RRF: the vector-scored chunk collects a rank from both arms and overtakes the
        // BM25 leader — proof the pool re-rank ran after the corpus arm failed.
        Assert.Equal(rankedByVector.Id, results[0].Id);
    }

    [Fact]
    public async Task TickerScoped_AutoMode_DuplicateBm25Rows_ReturnsEachChunkOnce()
    {
        var chunkId = Guid.NewGuid();
        var bestRankedInstance = new Chunk
        {
            Id = chunkId,
            Ticker = "AAPL",
            Content = "best-ranked materialization",
        };
        var duplicateInstance = new Chunk
        {
            Id = chunkId,
            Ticker = "AAPL",
            Content = "duplicate materialization",
        };
        var chunkRepository = new StubChunkRepository(
            bm25Results: [bestRankedInstance, duplicateInstance],
            allChunks: [bestRankedInstance]
        );
        var embeddingRepository = new StubEmbeddingRepository(similarChunkIds: [chunkId]);
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search("semantic question", 5, ticker: "AAPL");

        var result = Assert.Single(results);
        Assert.Same(bestRankedInstance, result);
    }

    [Fact]
    public async Task TickerScoped_AutoMode_DuplicateBm25Rows_DoNotInflateRrfScore()
    {
        var duplicate = new Chunk
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Content = "keyword-only result",
        };
        var semanticMatch = new Chunk
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Content = "semantic match",
        };
        var chunkRepository = new StubChunkRepository(
            bm25Results: [duplicate, duplicate, semanticMatch],
            allChunks: [duplicate, semanticMatch]
        );
        var embeddingRepository = new StubEmbeddingRepository(
            similarChunkIds: [semanticMatch.Id]
        );
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search("semantic question", 5, ticker: "AAPL");

        Assert.Equal(semanticMatch.Id, results[0].Id);
        Assert.Equal(2, results.Count);
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
