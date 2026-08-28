using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// Helpers that convert a value observed as-of a past date onto today's
/// post-split basis, using the set of splits that have occurred since.
/// </summary>
public static class SplitAdjustment
{
    /// <summary>
    /// Factor that converts a share COUNT observed as-of <paramref name="asOf"/>
    /// onto today's basis. Each split with an <see cref="StockSplit.EffectiveDate"/>
    /// strictly after <paramref name="asOf"/> multiplies the count by its
    /// <see cref="StockSplit.Numerator"/>/<see cref="StockSplit.Denominator"/>
    /// ratio. The comparison is strict (<c>&gt;</c>) because a report dated on the
    /// effective date already reflects the post-split count. Splits with a
    /// non-positive ratio arm are skipped because they cannot describe a split.
    /// </summary>
    public static decimal ShareCountFactor(DateOnly asOf, IEnumerable<StockSplit> splits)
    {
        var factor = 1m;
        foreach (var split in splits)
        {
            if (split.EffectiveDate > asOf && split.Numerator > 0 && split.Denominator > 0)
            {
                factor *= split.Numerator / split.Denominator;
            }
        }
        return factor;
    }

    /// <summary>
    /// Restates a share COUNT observed as-of <paramref name="asOf"/> onto today's
    /// basis by multiplying it by <see cref="ShareCountFactor"/> and rounding to the
    /// nearest whole share. A no-op (returns <paramref name="count"/> unchanged) when
    /// the factor is 1 — i.e. the stock has no splits after <paramref name="asOf"/> —
    /// so an unsplit stock is never perturbed by rounding.
    /// </summary>
    public static long AdjustShareCount(
        long count,
        DateOnly asOf,
        IEnumerable<StockSplit> splits
    ) => AdjustShareCount(count, ShareCountFactor(asOf, splits));

    /// <summary>
    /// Restates a share COUNT by an already-computed <paramref name="shareCountFactor"/>
    /// (see <see cref="ShareCountFactor"/>), rounding to the nearest whole share. A no-op
    /// when the factor is 1. Sign is preserved, so a negative count (e.g. a change/delta)
    /// restates correctly. Kept as a separate overload so callers that render many rows for
    /// a single as-of date compute the factor once and reuse it.
    /// </summary>
    public static long AdjustShareCount(long count, decimal shareCountFactor) =>
        shareCountFactor == 1m
            ? count
            : (long)Math.Round(count * shareCountFactor, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Restates a fractional share count without discarding FINRA's sub-share precision.
    /// Presentation layers may still round the result when they intentionally display N0.
    /// </summary>
    public static decimal AdjustShareCount(decimal count, decimal shareCountFactor) =>
        count * shareCountFactor;

    /// <summary>
    /// Factor that converts a PRICE observed as-of <paramref name="asOf"/> onto
    /// today's basis — the inverse of <see cref="ShareCountFactor"/>, since a
    /// split moves price opposite to share count. Returns 1 when the share-count
    /// factor is zero to avoid a divide-by-zero.
    /// </summary>
    public static decimal PriceFactor(DateOnly asOf, IEnumerable<StockSplit> splits)
    {
        var f = ShareCountFactor(asOf, splits);
        return f == 0m ? 1m : 1m / f;
    }

    /// <summary>
    /// Restates a per-share value filed on <paramref name="filedDate"/> onto today's share
    /// basis. Filings issued after a split already restate comparative per-share figures, so
    /// only splits effective strictly after the filing date move the value.
    /// </summary>
    public static decimal AdjustPerShareValue(
        decimal value,
        DateOnly filedDate,
        IEnumerable<StockSplit> splits
    ) => AdjustPerShareValue(value, ShareCountFactor(filedDate, splits));

    /// <summary>
    /// Restates a per-share value using an already-computed share-count factor. A zero factor
    /// leaves the value unchanged rather than dividing by zero.
    /// </summary>
    public static decimal AdjustPerShareValue(decimal value, decimal shareCountFactor) =>
        shareCountFactor == 0m ? value : value / shareCountFactor;
}
