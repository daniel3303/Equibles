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
    /// Selects the splits that changed a count observed on one listed series (13F, insider, short),
    /// keyed by ticker like <see cref="PriceSeriesSplitScope.ForListing(IEnumerable{StockSplit}, string, string)"/>.
    /// A narrower rule than <see cref="ForListing(IEnumerable{StockSplit}, Guid)"/>: only the primary
    /// series adopts the event's confirmed row from the issuer's other rows, and only when its own rows
    /// of the event are all contradicted. A series with no row of the event adopts nothing, because a depositary receipt
    /// can absorb an ordinary-share event in its ratio.
    /// </summary>
    public static List<StockSplit> ForListedSeries(
        IEnumerable<StockSplit> splits,
        string primaryTicker,
        string listedTicker
    )
    {
        var all = splits?.ToList() ?? [];
        var own = PriceSeriesSplitScope.ForListing(all, primaryTicker, listedTicker);
        return string.Equals(primaryTicker, listedTicker, StringComparison.OrdinalIgnoreCase)
            ? AdoptConfirmedEvents(all, own)
            : own;
    }

    /// <summary>
    /// <see cref="ForListedSeries(IEnumerable{StockSplit}, string, string)"/> for a caller that
    /// knows the listing by ID; <paramref name="primary"/> marks the issuer's presentation listing.
    /// </summary>
    public static List<StockSplit> ForListedSeries(
        IEnumerable<StockSplit> splits,
        Guid listingId,
        bool primary
    )
    {
        var all = splits?.ToList() ?? [];
        var own = PriceSeriesSplitScope.ForListing(all, listingId);
        return primary ? AdoptConfirmedEvents(all, own) : own;
    }

    private static List<StockSplit> AdoptConfirmedEvents(List<StockSplit> all, List<StockSplit> own)
    {
        if (own.Count == 0)
            return own;
        // Callers may pass several issuers' rows; another issuer's event is never this series'.
        var issuers = own.Select(split => split.EquityIssuerId).ToHashSet();
        var scoped = new List<StockSplit>(own);
        foreach (
            var rows in SplitEvents.Group(
                all.Where(split => issuers.Contains(split.EquityIssuerId))
            )
        )
        {
            var ownRows = rows.Where(own.Contains).ToList();
            if (
                ownRows.Count == 0
                || ownRows.Any(split =>
                    split.ShareCountEvidence != StockSplitShareCountEvidence.Contradicted
                )
            )
                continue;
            var confirmed = rows.FirstOrDefault(split =>
                split.ShareCountEvidence == StockSplitShareCountEvidence.Confirmed
                && !own.Contains(split)
            );
            if (confirmed == null)
                continue;
            // Replace, not add: the contradicted row shares the event's date and would read as a
            // second split on that day to every unresolved-basis check.
            scoped.RemoveAll(ownRows.Contains);
            scoped.Add(confirmed);
        }
        return scoped.OrderBy(split => split.EffectiveDate).ToList();
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
