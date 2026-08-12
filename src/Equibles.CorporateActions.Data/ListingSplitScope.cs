using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// One listing's captured splits, prepared for restating raw closes onto the current post-split
/// basis. The price factor is Denominator/Numerator — the inverse of
/// <see cref="SplitAdjustment.ShareCountFactor"/> — applied for every restatable split strictly
/// after the close's date (a close ON the effective date already trades post-split, matching the
/// share-side convention). A split whose ratio cannot be trusted (non-positive numerator or
/// denominator) cannot restate anything across it, so it stays an exclusion boundary: closes
/// before <see cref="UnusableBoundary"/> must be dropped (absent beats wrong).
/// </summary>
public sealed class ListingSplitScope
{
    public static readonly ListingSplitScope Empty = new([], null);

    private readonly IReadOnlyList<StockSplit> _restatable;

    private ListingSplitScope(IReadOnlyList<StockSplit> restatable, DateOnly? unusableBoundary)
    {
        _restatable = restatable;
        UnusableBoundary = unusableBoundary;
    }

    /// <summary>Latest split with an unusable ratio; closes before it must be dropped.</summary>
    public DateOnly? UnusableBoundary { get; }

    public static ListingSplitScope Of(IEnumerable<StockSplit> scopedSplits)
    {
        if (scopedSplits == null)
            return Empty;

        List<StockSplit> restatable = null;
        DateOnly? unusableBoundary = null;
        foreach (var split in scopedSplits)
        {
            if (split.Numerator > 0 && split.Denominator > 0)
            {
                (restatable ??= []).Add(split);
            }
            else if (unusableBoundary == null || split.EffectiveDate > unusableBoundary)
            {
                unusableBoundary = split.EffectiveDate;
            }
        }
        if (restatable == null && unusableBoundary == null)
            return Empty;
        return new ListingSplitScope((IReadOnlyList<StockSplit>)restatable ?? [], unusableBoundary);
    }

    /// <summary>
    /// Restates a raw close observed on <paramref name="date"/> onto the current basis. A close
    /// with no restatable split after it returns unchanged.
    /// </summary>
    public decimal RestateClose(decimal close, DateOnly date)
    {
        var factor = 1m;
        foreach (var split in _restatable)
        {
            if (split.EffectiveDate > date)
                factor *= split.Denominator / split.Numerator;
        }
        return factor == 1m ? close : close * factor;
    }
}
