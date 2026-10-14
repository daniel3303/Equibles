using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

// A phrase search is a filter. The recall aids of a ranked search would admit excerpts without
// the phrase, so a phrase search runs one phrase pass and nothing else, and a timeout is surfaced
// rather than answered from a broader pass.
public class HybridChunkSearcherPhraseMatchTests
{
    [Fact]
    public async Task PhraseMatch_RunsOnlyThePhrasePass_EvenWhenResultsAreSparse()
    {
        var match = new Chunk { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var chunks = new PhraseChunkRepository(phraseResults: [match]);
        var embeddings = new StubEmbeddingRepository(similarChunkIds: [Guid.NewGuid()]);

        var results = await NewSearcher(chunks, embeddings)
            .Search(
                "data center revenue",
                10,
                ticker: "AAPL",
                disjunctiveFallback: true,
                match: SearchMatch.Phrase
            );

        Assert.Equal([match.Id], results.Select(chunk => chunk.Id));
        Assert.Equal(["data center revenue"], chunks.PhraseQueries);
        Assert.Equal(0, chunks.RankedPasses);
        Assert.False(embeddings.SearchSimilarChunksCalled);
    }

    [Fact]
    public async Task PhraseMatch_SurfacesATimeout_InsteadOfBroadening()
    {
        var chunks = new PhraseChunkRepository(phraseResults: [], phraseTimesOut: true);
        var searcher = NewSearcher(chunks, new StubEmbeddingRepository(similarChunkIds: []));

        await Assert.ThrowsAsync<ChunkSearchTimeoutException>(() =>
            searcher.Search(
                "data center revenue",
                10,
                ticker: "AAPL",
                disjunctiveFallback: true,
                match: SearchMatch.Phrase
            )
        );
        Assert.Equal(0, chunks.RankedPasses);
        Assert.Equal(0, chunks.ScopedFallbacks);
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

    private sealed class PhraseChunkRepository(
        List<Chunk> phraseResults,
        bool phraseTimesOut = false
    ) : ChunkRepository(null)
    {
        public List<string> PhraseQueries { get; } = [];
        public int RankedPasses { get; private set; }
        public int ScopedFallbacks { get; private set; }

        public override Task<List<Chunk>> PhraseSearch(
            string phrase,
            int maxResults,
            string ticker = null,
            IReadOnlyCollection<string> excludeTickers = null,
            Guid? documentId = null,
            IReadOnlyCollection<DocumentType> documentTypes = null,
            DateOnly? startDate = null,
            DateOnly? endDate = null,
            int? commandTimeoutSeconds = null,
            CancellationToken cancellationToken = default
        )
        {
            PhraseQueries.Add(phrase);
            if (phraseTimesOut)
                throw new ChunkSearchTimeoutException(
                    "statement budget elapsed",
                    new TimeoutException()
                );
            return Task.FromResult(phraseResults);
        }

        public override Task<List<Chunk>> HybridSearch(
            string searchText,
            int maxResults,
            string ticker = null,
            IReadOnlyCollection<string> excludeTickers = null,
            Guid? documentId = null,
            IReadOnlyCollection<DocumentType> documentTypes = null,
            DateOnly? startDate = null,
            DateOnly? endDate = null,
            bool conjunctive = true,
            int? commandTimeoutSeconds = null,
            CancellationToken cancellationToken = default
        )
        {
            RankedPasses++;
            return Task.FromResult(new List<Chunk> { new() { Id = Guid.NewGuid() } });
        }

        public override Task<List<Chunk>> HybridSearchScopedFallback(
            string searchText,
            int maxResults,
            string ticker = null,
            Guid? documentId = null,
            IReadOnlyCollection<DocumentType> documentTypes = null,
            DateOnly? startDate = null,
            DateOnly? endDate = null,
            CancellationToken cancellationToken = default
        )
        {
            ScopedFallbacks++;
            return Task.FromResult(new List<Chunk> { new() { Id = Guid.NewGuid() } });
        }
    }
}
