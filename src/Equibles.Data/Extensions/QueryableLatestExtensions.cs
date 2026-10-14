using System.Linq.Expressions;

namespace Equibles.Data.Extensions;

public static class QueryableLatestExtensions
{
    /// <summary>
    /// Projects the source to <paramref name="selector"/> and returns the single
    /// highest value as an <see cref="IQueryable{TKey}"/> (ORDER BY ... DESC LIMIT 1).
    /// Never add DISTINCT: it cannot change the maximum, and Postgres plans the
    /// DISTINCT subquery as a full scan instead of one backward index probe.
    /// </summary>
    public static IQueryable<TKey> LatestValue<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> selector
    )
    {
        return source.Select(selector).OrderByDescending(k => k).Take(1);
    }
}
