using System.Linq.Expressions;

namespace Equibles.Data.Extensions;

public static class QueryableLatestExtensions
{
    /// <summary>
    /// Projects the source to <paramref name="selector"/> and returns the single
    /// highest value as an <see cref="IQueryable{TKey}"/> (ORDER BY ... DESC LIMIT 1).
    /// Set <paramref name="distinct"/> when the underlying rows can repeat the
    /// projected value and the caller wants a unique-by-projection comparison.
    /// </summary>
    public static IQueryable<TKey> LatestValue<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> selector,
        bool distinct = false
    )
    {
        var projected = source.Select(selector);
        if (distinct)
        {
            projected = projected.Distinct();
        }
        return projected.OrderByDescending(k => k).Take(1);
    }
}
