using System.Linq.Expressions;

namespace Equibles.Holdings.Repositories.Extensions;

internal static class QueryableExtensions
{
    // Applies the predicate only when the condition holds, so optional filters can be
    // chained without an if-statement per criterion. The predicate is left out of the
    // query tree entirely when the condition is false, matching a skipped Where call.
    public static IQueryable<T> WhereIf<T>(
        this IQueryable<T> source,
        bool condition,
        Expression<Func<T, bool>> predicate
    )
    {
        return condition ? source.Where(predicate) : source;
    }
}
