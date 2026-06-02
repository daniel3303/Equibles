namespace Equibles.Data.Extensions;

public static class EnumerableLatestExtensions
{
    /// <summary>
    /// Groups <paramref name="source"/> by <paramref name="keySelector"/> and returns the
    /// single row with the highest <paramref name="dateSelector"/> value from each group —
    /// the latest record per key. Runs in memory; call it after materialising an EF query,
    /// never against an <see cref="IQueryable{T}"/> (the equivalent EF pattern must stay
    /// inline so its expression tree still translates to SQL).
    /// </summary>
    public static IEnumerable<TSource> LatestPerGroup<TSource, TKey, TDate>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TDate> dateSelector
    )
    {
        return source
            .GroupBy(keySelector)
            .Select(group => group.OrderByDescending(dateSelector).First());
    }
}
