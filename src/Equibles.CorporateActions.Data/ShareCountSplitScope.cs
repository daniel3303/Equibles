using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// Selects the splits that changed an issuer's share count, as seen from one listing, for
/// restating facts. A contradicted row is a price factor and never moves a count; a confirmed row
/// on another listing is the issuer's event too and is adopted when the listing has none of its own.
/// </summary>
public static class ShareCountSplitScope
{
    public static List<StockSplit> ForListing(IEnumerable<StockSplit> splits, Guid listingId)
    {
        var candidates =
            splits
                ?.Where(split =>
                    split.ShareCountEvidence != StockSplitShareCountEvidence.Contradicted
                    && (
                        split.EquityListingId == listingId
                        || split.ShareCountEvidence == StockSplitShareCountEvidence.Confirmed
                    )
                )
                .ToList()
            ?? [];
        return SplitEvents
            .Group(candidates)
            .SelectMany(rows =>
                rows.Any(split => split.EquityListingId == listingId)
                    ? rows.Where(split => split.EquityListingId == listingId)
                    : rows.Take(1)
            )
            .ToList();
    }

    /// <summary>
    /// True when a split after <paramref name="asOf"/> cannot be attributed: an unjudged row with
    /// no listing, or own rows that disagree on one date or cannot describe a split.
    /// </summary>
    public static bool HasUnresolvedBasis(
        IEnumerable<StockSplit> splits,
        Guid listingId,
        DateOnly asOf
    )
    {
        var all = splits?.ToList() ?? [];
        if (
            all.Any(split =>
                split.EffectiveDate > asOf
                && split.EquityListingId == null
                && split.ShareCountEvidence == null
            )
        )
            return true;
        var after = ForListing(all, listingId).Where(split => split.EffectiveDate > asOf).ToList();
        return after.Any(split => SplitEvents.Ratio(split) == null)
            || after.GroupBy(split => split.EffectiveDate).Any(group => group.Count() > 1);
    }
}
