using System.Linq.Expressions;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore.Query;

namespace Equibles.UnitTests.Sec;

// Shared stubs for the HybridChunkSearcher unit tests: canned BM25 results, a spy on the
// corpus vector arm, and the minimal async-queryable plumbing the fused-id materialization
// needs. Shared by the document-scoped and Auto-vector-source test classes.
internal sealed class StubChunkRepository : ChunkRepository
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

// BM25 stub whose passes can individually time out — models the cold-index case where the
// per-statement budget fires. A pass that doesn't time out serves its canned results.
internal sealed class TimeoutChunkRepository : ChunkRepository
{
    private readonly bool _conjunctiveTimesOut;
    private readonly bool _disjunctiveTimesOut;
    private readonly List<Chunk> _conjunctiveResults;
    private readonly List<Chunk> _disjunctiveResults;
    private readonly List<Chunk> _allChunks;

    public TimeoutChunkRepository(
        bool conjunctiveTimesOut = false,
        bool disjunctiveTimesOut = false,
        List<Chunk> conjunctiveResults = null,
        List<Chunk> disjunctiveResults = null,
        List<Chunk> allChunks = null
    )
        : base(null)
    {
        _conjunctiveTimesOut = conjunctiveTimesOut;
        _disjunctiveTimesOut = disjunctiveTimesOut;
        _conjunctiveResults = conjunctiveResults ?? [];
        _disjunctiveResults = disjunctiveResults ?? [];
        _allChunks = allChunks ?? [];
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
        if (conjunctive ? _conjunctiveTimesOut : _disjunctiveTimesOut)
            throw new ChunkSearchTimeoutException(
                "statement budget elapsed",
                new TimeoutException()
            );
        return Task.FromResult(conjunctive ? _conjunctiveResults : _disjunctiveResults);
    }

    public override IQueryable<Chunk> GetAll() => new TestAsyncEnumerable<Chunk>(_allChunks);
}

internal sealed class StubEmbeddingRepository : EmbeddingRepository
{
    private readonly List<Guid> _similarChunkIds;
    private readonly bool _throwOnCorpusSearch;
    private readonly List<Embedding> _storedEmbeddings;

    public bool SearchSimilarChunksCalled { get; private set; }

    public string SearchSimilarChunksTicker { get; private set; }

    public StubEmbeddingRepository(
        List<Guid> similarChunkIds,
        bool throwOnCorpusSearch = false,
        List<Embedding> storedEmbeddings = null
    )
        : base(null)
    {
        _similarChunkIds = similarChunkIds;
        _throwOnCorpusSearch = throwOnCorpusSearch;
        _storedEmbeddings = storedEmbeddings ?? [];
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
        SearchSimilarChunksTicker = ticker;
        if (_throwOnCorpusSearch)
            throw new InvalidOperationException("corpus vector arm unavailable");
        return Task.FromResult(_similarChunkIds);
    }

    // The pool re-rank's stored-vector lookup, served from the in-memory list so the
    // corpus-arm fallback path is exercisable without a database.
    public override IQueryable<Embedding> GetByChunks(IEnumerable<Chunk> chunks)
    {
        var ids = chunks.Select(chunk => chunk.Id).ToHashSet();
        return new TestAsyncEnumerable<Embedding>(
            _storedEmbeddings.Where(embedding => ids.Contains(embedding.ChunkId))
        );
    }
}

// Minimal in-memory IQueryable implementing EF Core's async query surface, so the
// fused-id materialization runs over a plain list.
internal sealed class TestAsyncEnumerable<T>
    : EnumerableQuery<T>,
        IAsyncEnumerable<T>,
        IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable)
        : base(enumerable) { }

    public TestAsyncEnumerable(Expression expression)
        : base(expression) { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

internal sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
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

internal sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
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
