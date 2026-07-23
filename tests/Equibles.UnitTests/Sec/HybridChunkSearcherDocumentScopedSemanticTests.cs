using System.Linq.Expressions;
using Equibles.Data;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore.Query;
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
            Options.Create(new HybridSearchOptions()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            NullLogger<HybridChunkSearcher>.Instance
        );
    }

    private sealed class StubChunkRepository : ChunkRepository
    {
        private readonly List<Chunk> _bm25Results;
        private readonly List<Chunk> _allChunks;

        public StubChunkRepository(List<Chunk> bm25Results, List<Chunk> allChunks)
            : base(null)
        {
            _bm25Results = bm25Results;
            _allChunks = allChunks;
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
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(_bm25Results);
        }

        public override IQueryable<Chunk> GetAll() => new TestAsyncEnumerable<Chunk>(_allChunks);
    }

    private sealed class StubEmbeddingRepository : EmbeddingRepository
    {
        private readonly List<Guid> _similarChunkIds;

        public bool SearchSimilarChunksCalled { get; private set; }

        public StubEmbeddingRepository(List<Guid> similarChunkIds)
            : base(null)
        {
            _similarChunkIds = similarChunkIds;
        }

        public override Task<List<Guid>> SearchSimilarChunks(
            float[] queryEmbedding,
            string model,
            int maxResults,
            string ticker = null,
            Guid? documentId = null,
            DocumentType documentType = null,
            DateTime? startUtc = null,
            DateTime? endUtc = null,
            CancellationToken cancellationToken = default
        )
        {
            SearchSimilarChunksCalled = true;
            return Task.FromResult(_similarChunkIds);
        }
    }

    // Minimal in-memory IQueryable implementing EF Core's async query surface, so the
    // fused-id materialization runs over a plain list.
    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable)
            : base(enumerable) { }

        public TestAsyncEnumerable(Expression expression)
            : base(expression) { }

        public IAsyncEnumerator<T> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        ) => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        internal TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) =>
            new TestAsyncEnumerable<TEntity>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new TestAsyncEnumerable<TElement>(expression);

        public object Execute(Expression expression) => _inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(
            Expression expression,
            CancellationToken cancellationToken = default
        )
        {
            var expectedResultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = _inner.Execute(expression);
            return (TResult)
                typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(expectedResultType)
                    .Invoke(null, [executionResult])!;
        }
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;

        public T Current => _inner.Current;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
