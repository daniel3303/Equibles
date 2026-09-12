using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// Selects captured splits that belong to one exact listed price series.
/// </summary>
public static class PriceSeriesSplitScope
{
    /// <summary>
    /// An unqualified source symbol refers to a U.S. listing; unknown attribution is never primary evidence.
    /// </summary>
    public static List<StockSplit> ForListing(
        IEnumerable<StockSplit> splits,
        string primaryTicker,
        string listedTicker
    )
    {
        if (splits == null || string.IsNullOrWhiteSpace(listedTicker))
            return [];

        var matching = splits
            .Where(split =>
                split.PriceSeriesTicker != null
                && (split.EquityListingId == null || split.Listing?.MarketCountryCode == "US")
                && string.Equals(
                    split.EquityListingId == null ? split.PriceSeriesTicker : split.Listing.Ticker,
                    listedTicker,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();
        return
            matching
                .Where(split => split.EquityListingId != null)
                .Select(split => split.EquityListingId)
                .Distinct()
                .Take(2)
                .Count() > 1
            ? []
            : matching
                .GroupBy(split => split.EffectiveDate)
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .ToList();
    }

    public static List<StockSplit> ForListing(IEnumerable<StockSplit> splits, Guid listingId) =>
        splits?.Where(split => split.EquityListingId == listingId).ToList() ?? [];

    public static bool HasUnresolvedBasis(
        IEnumerable<StockSplit> splits,
        Guid listingId,
        DateOnly asOf
    )
    {
        var after = splits?.Where(split => split.EffectiveDate > asOf).ToList() ?? [];
        if (after.Any(split => split.EquityListingId == null))
            return true;
        var matching = ForListing(after, listingId);
        return matching.Any(split => split.Numerator <= 0 || split.Denominator <= 0)
            || matching.GroupBy(split => split.EffectiveDate).Any(group => group.Count() > 1);
    }

    public static bool HasUnresolvedBasis(
        IEnumerable<StockSplit> splits,
        string listedTicker,
        DateOnly asOf
    )
    {
        var all = splits?.ToList() ?? [];
        var after = all.Where(split => split.EffectiveDate > asOf).ToList();
        if (
            after.Any(split =>
                split.PriceSeriesTicker == null
                || split.EquityListingId != null && split.Listing == null
            )
        )
            return true;
        var matching = ForPriceComparison(after, listedTicker);
        return matching.Any(split => split.Numerator <= 0 || split.Denominator <= 0)
            || matching.GroupBy(split => split.EffectiveDate).Any(group => group.Count() > 1)
            || matching.Count > 0
                && ForPriceComparison(all, listedTicker)
                    .Where(split => split.EquityListingId != null)
                    .Select(split => split.EquityListingId)
                    .Distinct()
                    .Skip(1)
                    .Any();
    }

    /// <summary>
    /// Dates that raw prices cannot safely cross. Unknown attribution remains an exclusion
    /// boundary for every issuer listing; these rows must never supply a restatement ratio.
    /// </summary>
    public static List<StockSplit> ForPriceComparison(
        IEnumerable<StockSplit> splits,
        string listedTicker
    ) =>
        splits
            ?.Where(split =>
                split.PriceSeriesTicker == null
                || split.EquityListingId != null && split.Listing == null
                || (split.EquityListingId == null || split.Listing.MarketCountryCode == "US")
                    && string.Equals(
                        split.EquityListingId == null
                            ? split.PriceSeriesTicker
                            : split.Listing.Ticker,
                        listedTicker,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .ToList()
        ?? [];
}
