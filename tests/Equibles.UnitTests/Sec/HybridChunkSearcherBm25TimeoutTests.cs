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
    public async Task ConjunctiveTimeout_TickerScoped_CompanyFallbackReturnsWithoutOtherPasses()
    {
        var chunk = new Chunk
        {
            Id = Guid.NewGuid(),
            Ticker = "TSM",
            Content = "bounded company-local match",
        };
        var chunkRepository = new TimeoutChunkRepository(
            conjunctiveTimesOut: true,
            companyFallbackResults: [chunk],
            allChunks: [chunk]
        );
        var embeddingRepository = new StubEmbeddingRepository([]);
        var searcher = NewSearcher(chunkRepository, embeddingRepository);

        var results = await searcher.Search("10-K", 5, ticker: "TSM", disjunctiveFallback: true);

        Assert.Equal(chunk.Id, Assert.Single(results).Id);
        Assert.Equal(3, Assert.Single(chunkRepository.ConjunctiveBudgets));
        Assert.Equal(1, chunkRepository.CompanyFallbackCalls);
        Assert.Empty(chunkRepository.DisjunctiveBudgets);
        Assert.False(embeddingRepository.SearchSimilarChunksCalled);
    }

    [Fact]
    public async Task CompanyFallback_CallerCancellationEscapes()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var chunkRepository = new TimeoutChunkRepository(
            conjunctiveTimesOut: true,
            companyFallbackError: new OperationCanceledException(cancellation.Token)
        );
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            searcher.Search("query", 5, ticker: "AAPL", cancellationToken: cancellation.Token)
        );
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
    public async Task DuplicateConjunctiveRows_DoNotSuppressDisjunctiveFallback()
    {
        var precise = new Chunk { Id = Guid.NewGuid(), Content = "precise match" };
        var broad = new Chunk { Id = Guid.NewGuid(), Content = "broadened match" };
        var chunkRepository = new TimeoutChunkRepository(
            conjunctiveResults: [precise, precise, precise, precise, precise],
            disjunctiveResults: [precise, broad],
            allChunks: [precise, broad]
        );
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        var results = await searcher.Search("query", 5, disjunctiveFallback: true);

        Assert.Equal([precise.Id, broad.Id], results.Select(chunk => chunk.Id));
        Assert.Single(chunkRepository.DisjunctiveBudgets);
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
    public async Task ConjunctiveCompletesEmpty_DisjunctiveTimeout_SurfacesTheTimeout()
    {
        // The conjunctive pass proved nothing (its empty is a SUBSET question — the
        // disjunctive superset might still have matched), so a timed-out fallback leaves
        // the empty unproven and the timeout must surface.
        var chunkRepository = new TimeoutChunkRepository(disjunctiveTimesOut: true);
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        await Assert.ThrowsAsync<ChunkSearchTimeoutException>(() =>
            searcher.Search("query", 5, disjunctiveFallback: true)
        );
    }

    [Fact]
    public async Task NonTimeoutFault_EscapesUntouched()
    {
        // The degrade is strictly for timeouts — a real fault must not be swallowed
        // into an empty pool.
        var chunkRepository = new TimeoutChunkRepository(
            conjunctiveError: new InvalidOperationException("index corrupt")
        );
        var searcher = NewSearcher(chunkRepository, new StubEmbeddingRepository([]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            searcher.Search("query", 5, disjunctiveFallback: true)
        );
        Assert.Empty(chunkRepository.DisjunctiveBudgets);
    }

    [Fact]
    public async Task ConjunctiveTimeout_FallbackRunsOnTheTightenedBudget()
    {
        // The pass after a timed-out pass gets a reduced statement budget so the pair
        // stays bounded; a fallback after a HEALTHY short pass keeps the default.
        var chunk = new Chunk { Id = Guid.NewGuid(), Content = "broadened match" };
        var timedOut = new TimeoutChunkRepository(
            conjunctiveTimesOut: true,
            disjunctiveResults: [chunk],
            allChunks: [chunk]
        );
        await NewSearcher(timedOut, new StubEmbeddingRepository([]))
            .Search("query", 5, disjunctiveFallback: true);

        var healthy = new TimeoutChunkRepository(disjunctiveResults: [chunk], allChunks: [chunk]);
        await NewSearcher(healthy, new StubEmbeddingRepository([]))
            .Search("query", 5, disjunctiveFallback: true);

        Assert.NotNull(Assert.Single(timedOut.DisjunctiveBudgets));
        Assert.Null(Assert.Single(healthy.DisjunctiveBudgets));
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
