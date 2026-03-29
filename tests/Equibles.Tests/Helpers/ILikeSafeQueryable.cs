using System.Linq.Expressions;
using System.Reflection;

namespace Equibles.Tests.Helpers;

/// <summary>
/// An in-memory IQueryable that rewrites EF.Functions.ILike expression tree nodes
/// into case-insensitive string.Contains calls so that LINQ-to-Objects can evaluate
/// queries designed for PostgreSQL without throwing InvalidOperationException.
///
/// Usage: wrap a collection in ILikeSafeQueryable instead of TestAsyncQueryable
/// when the query chain includes EF.Functions.ILike calls.
/// </summary>
public class ILikeSafeQueryable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryProvider {
    public ILikeSafeQueryable(IEnumerable<T> enumerable) : base(enumerable) { }
    public ILikeSafeQueryable(Expression expression) : base(expression) { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
        return new AsyncEnumeratorAdapter(this.AsEnumerable().GetEnumerator());
    }

    IQueryable IQueryProvider.CreateQuery(Expression expression) {
        return new ILikeSafeQueryable<T>(ILikeRewriter.Instance.Visit(expression));
    }

    IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression) {
        return new ILikeSafeQueryable<TElement>(ILikeRewriter.Instance.Visit(expression));
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

/// <summary>
/// Rewrites EF.Functions.ILike(text, pattern) into
/// text.Contains(trimmedPattern, StringComparison.OrdinalIgnoreCase)
/// where trimmedPattern has leading/trailing '%' wildcards stripped.
/// </summary>
file sealed class ILikeRewriter : ExpressionVisitor {
    public static readonly ILikeRewriter Instance = new();

    protected override Expression VisitMethodCall(MethodCallExpression node) {
        if (node.Method.Name == "ILike" && node.Arguments.Count >= 3) {
            // EF.Functions.ILike(dbFunctions, matchExpression, pattern)
            var text = Visit(node.Arguments[1]);
            var pattern = Visit(node.Arguments[2]);

            // Strip leading/trailing '%' from the pattern at runtime
            var stripMethod = typeof(ILikeRewriter).GetMethod(nameof(StripWildcards),
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var strippedPattern = Expression.Call(stripMethod, pattern);

            var containsMethod = typeof(string).GetMethod(nameof(string.Contains),
                [typeof(string), typeof(StringComparison)])!;

            return Expression.Call(
                text,
                containsMethod,
                strippedPattern,
                Expression.Constant(StringComparison.OrdinalIgnoreCase));
        }

        return base.VisitMethodCall(node);
    }

    private static string StripWildcards(string pattern) {
        if (pattern == null) return "";
        return pattern.Trim('%');
    }
}
