using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// Selects the splits that changed an issuer's share count, as seen from one listing, for
/// restating facts. A contradicted row is a price factor and never moves a count; a confirmed row
/// on another listing is the issuer's event too and is adopted when the listing has none of its own.
/// </summary>
public static class ShareCountSplitScope
{
    public static List<StockSplit> ForListing(IEnumerable<StockSplit> splits, Guid listingId) =>
        SplitEvents
            .Group(splits ?? [])
            .SelectMany(rows =>
            {
                // Group before filtering: a dropped row can be the link that makes rows one event.
                var standing = rows.Where(split =>
                        split.ShareCountEvidence != StockSplitShareCountEvidence.Contradicted
                    )
                    .ToList();
                var own = standing.Where(split => split.EquityListingId == listingId).ToList();
                return own.Count > 0
                    ? own
                    : standing
                        .Where(split =>
                            split.ShareCountEvidence == StockSplitShareCountEvidence.Confirmed
                        )
                        .Take(1);
            })
            .ToList();

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
