using Equibles.Cboe.Data.Models;

namespace Equibles.Cboe.Repositories.Extensions;

public static class CboePutCallRatioQueryableExtensions
{
    /// <summary>
    /// Keeps only rows whose put/call ratio reconciles with its own volumes.
    /// A put/call ratio is puts ÷ calls, so a genuine row needs a positive call
    /// denominator and can never reach total volume (puts/calls ≤ puts &lt; total).
    /// A batch of historical VIX rows (all on or before 2019-10-04) was loaded
    /// with the ratio set to puts + total and the call volume either missing or a
    /// 1 sentinel; those values run into the millions and are not real ratios, so
    /// they must never be served on the historical series.
    /// </summary>
    public static IQueryable<CboePutCallRatio> OnlyReconcilable(
        this IQueryable<CboePutCallRatio> source
    )
    {
        return source.Where(r =>
            r.CallVolume != null
            && r.CallVolume > 0
            && r.PutCallRatio != null
            && r.TotalVolume != null
            && r.PutCallRatio < r.TotalVolume
        );
    }
}
