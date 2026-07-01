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
    /// non-positive denominator are skipped to guard against divide-by-zero.
    /// </summary>
    public static decimal ShareCountFactor(DateOnly asOf, IEnumerable<StockSplit> splits)
    {
        var factor = 1m;
        foreach (var split in splits)
        {
            if (split.EffectiveDate > asOf && split.Denominator > 0)
            {
                factor *= split.Numerator / split.Denominator;
            }
        }
        return factor;
    }

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
}
