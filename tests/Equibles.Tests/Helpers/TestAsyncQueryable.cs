using System.Linq.Expressions;

namespace Equibles.Tests.Helpers;

/// <summary>
/// In-memory IQueryable that also implements IAsyncEnumerable so that
/// EF Core async extension methods (ToDictionaryAsync, ToListAsync, etc.)
/// can operate on client-evaluated collections without requiring a real
/// database provider.
/// </summary>
public class TestAsyncQueryable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryProvider {
    public TestAsyncQueryable(IEnumerable<T> enumerable) : base(enumerable) { }
    public TestAsyncQueryable(Expression expression) : base(expression) { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
        return new AsyncEnumeratorAdapter(this.AsEnumerable().GetEnumerator());
    }

    IQueryable IQueryProvider.CreateQuery(Expression expression) {
        return new TestAsyncQueryable<T>(expression);
    }

    IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression) {
        return new TestAsyncQueryable<TElement>(expression);
    }

    private sealed class AsyncEnumeratorAdapter(IEnumerator<T> inner) : IAsyncEnumerator<T> {
        public T Current => inner.Current;

        public ValueTask DisposeAsync() {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync() {
            return new ValueTask<bool>(inner.MoveNext());
        }
    }
}
