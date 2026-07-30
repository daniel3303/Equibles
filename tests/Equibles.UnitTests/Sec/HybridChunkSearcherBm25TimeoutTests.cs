using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

// A BM25 pass that blows its statement budget (ChunkSearchTimeoutException — a cold index
// after a Postgres restart) must DEGRADE, not fail the whole search: the disjunctive
// fallback and the corpus vector arm still run. The timeout only resurfaces when the search
// would otherwise return empty — an unproven empty would read as "the filings say nothing
// about this", the exact lie the searcher exists to avoid.
public class HybridChunkSearcherBm25TimeoutTests
{
    [Fact]
    public async Task ConjunctiveTimeout_DisjunctiveFallbackCarriesTheResult()
    {
        var chunk = new Chunk { Id = Guid.NewGuid(), Content = "broadened match" };
        var chunkRepository = new TimeoutChunkRepository(
            conjunctiveTimesOut: true,
            disjunctiveResults: [chunk],
            allChunks: [chunk]
        );
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        var results = await searcher.Search(
            "long natural language query",
            5,
            disjunctiveFallback: true
        );

        Assert.Single(results);
        Assert.Equal(chunk.Id, results[0].Id);
    }

    [Fact]
    public async Task ConjunctiveTimeout_TickerScoped_CorpusVectorArmCarriesTheResult()
    {
        var chunk = new Chunk
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Content = "semantic match",
        };
        var chunkRepository = new TimeoutChunkRepository(
            conjunctiveTimesOut: true,
            allChunks: [chunk]
        );
        var embeddingRepository = new StubEmbeddingRepository(similarChunkIds: [chunk.Id]);
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search("purely semantic question", 5, ticker: "AAPL");

        Assert.Single(results);
        Assert.Equal(chunk.Id, results[0].Id);
        Assert.True(embeddingRepository.SearchSimilarChunksCalled);
    }

    [Fact]
    public async Task ConjunctiveSucceeds_DisjunctiveTimeout_KeepsTheConjunctiveResults()
    {
        var chunk = new Chunk { Id = Guid.NewGuid(), Content = "precise match" };
        var chunkRepository = new TimeoutChunkRepository(
            disjunctiveTimesOut: true,
            conjunctiveResults: [chunk],
            allChunks: [chunk]
        );
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        var results = await searcher.Search("query", 5, disjunctiveFallback: true);

        Assert.Single(results);
        Assert.Equal(chunk.Id, results[0].Id);
    }

    [Fact]
    public async Task ConjunctiveTimeout_DisjunctiveCompletesEmpty_ReturnsProvenEmptyWithoutThrowing()
    {
        // The disjunctive pass matches a SUPERSET of the conjunctive pass — when it
        // completes with nothing, "no matches" is proven and the earlier timeout is moot.
        var chunkRepository = new TimeoutChunkRepository(conjunctiveTimesOut: true);
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        var results = await searcher.Search("query matching nothing", 5, disjunctiveFallback: true);

        Assert.Empty(results);
    }

    [Fact]
    public async Task AllPassesTimeout_Unscoped_SurfacesTheTimeoutInsteadOfAFalseEmpty()
    {
        var chunkRepository = new TimeoutChunkRepository(
            conjunctiveTimesOut: true,
            disjunctiveTimesOut: true
        );
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        await Assert.ThrowsAsync<ChunkSearchTimeoutException>(() =>
            searcher.Search("long query", 5, disjunctiveFallback: true)
        );
    }

    [Fact]
    public async Task ConjunctiveTimeout_TickerScoped_CorpusArmEmpty_SurfacesTheTimeout()
    {
        // The vector arm ran but produced nothing to rank — the empty result is still
        // unproven, so the timeout must surface rather than read as "no matches".
        var chunkRepository = new TimeoutChunkRepository(conjunctiveTimesOut: true);
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        await Assert.ThrowsAsync<ChunkSearchTimeoutException>(() =>
            searcher.Search("semantic question", 5, ticker: "AAPL")
        );
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
            Options.Create(new HybridSearchOptions()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            NullLogger<HybridChunkSearcher>.Instance
        );
    }
}
