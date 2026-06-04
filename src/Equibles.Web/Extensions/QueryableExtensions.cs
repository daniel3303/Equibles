using System.Linq.Expressions;

namespace Equibles.Web.Extensions;

public static class QueryableExtensions
{
    // Order by the given key descending and cap at count — the "most recent N rows"
    // shape shared by the profile read actions. Returns IQueryable so callers can
    // still project before materializing.
    public static IQueryable<T> TakeMostRecent<T, TKey>(
        this IQueryable<T> source,
        Expression<Func<T, TKey>> orderKey,
        int count
    ) => source.OrderByDescending(orderKey).Take(count);
}
