using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// A captured split normally ends a raw-price window: stored bars carry no row-level basis
/// metadata, so bars on opposite sides of one may sit on different provider bases. The single
/// exception is a split whose price reconciliation is stamped applied for this exact listed
/// series: that stamp is written only after the full-history replacement rebased the whole
/// series in one transaction and re-checked every effective split, and the worker keeps
/// re-auditing a material marker against the stored closes at the boundary. Only the consumers
/// that opt in cross such a split; every latest-price contract deliberately still clips.
/// </summary>
public static class CertifiedSplitBasis
{
    /// <summary>
    /// The splits that still bound a raw-price window for <paramref name="listedTicker"/>:
    /// everything the caller scoped in, minus this series' own certified splits. A series whose
    /// own split history is ambiguous keeps every boundary it had.
    /// </summary>
    public static List<StockSplit> RemainingBoundaries(
        IReadOnlyList<StockSplit> applicableSplits,
        string listedTicker
    )
    {
        if (applicableSplits == null || applicableSplits.Count == 0)
            return [];
        if (string.IsNullOrWhiteSpace(listedTicker))
            return [.. applicableSplits];

        var ownSeries = applicableSplits
            .Where(split => BelongsToSeries(split, listedTicker))
            .ToList();
        // Two rows on one effective date leave the series' ratio ambiguous, and two listing IDs
        // leave the attribution itself unsettled. Neither can be certified from stored data.
        var duplicateDate = ownSeries
            .GroupBy(split => split.EffectiveDate)
            .Any(group => group.Count() > 1);
        var multipleListings =
            ownSeries.Select(split => split.EquityListingId).Distinct().Count() > 1;
        if (duplicateDate || multipleListings)
            return [.. applicableSplits];

        return applicableSplits.Where(split => !IsCertified(split, listedTicker)).ToList();
    }

    /// <summary>
    /// True when this split's own series was demonstrably rebased: it is bound to an exact US
    /// listing whose current symbol is <paramref name="listedTicker"/>, its ratio is usable, and
    /// its price reconciliation is stamped applied after the effective date.
    /// </summary>
    public static bool IsCertified(StockSplit split, string listedTicker) =>
        BelongsToSeries(split, listedTicker)
        && split.Numerator > 0m
        && split.Denominator > 0m
        && split.IsPriceAdjustmentApplied();

    // Legacy rows carry no listing, so their series can never be proved from stored data and they
    // stay boundaries, unlike ForPriceComparison, which may match them on PriceSeriesTicker.
    private static bool BelongsToSeries(StockSplit split, string listedTicker) =>
        split is { EquityListingId: not null, Listing.MarketCountryCode: "US" }
        && string.Equals(split.Listing.Ticker, listedTicker, StringComparison.OrdinalIgnoreCase);
}
