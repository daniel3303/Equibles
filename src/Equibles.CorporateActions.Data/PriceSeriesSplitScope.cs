using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// Selects captured splits that belong to one exact listed price series.
/// </summary>
public static class PriceSeriesSplitScope
{
    /// <summary>
    /// A primary series accepts exact current-ticker attribution plus legacy null attribution.
    /// A secondary series accepts exact attribution only.
    /// </summary>
    public static List<StockSplit> ForListing(
        IEnumerable<StockSplit> splits,
        string primaryTicker,
        string listedTicker
    )
    {
        if (splits == null || string.IsNullOrWhiteSpace(listedTicker))
            return [];

        var isPrimary = string.Equals(
            listedTicker,
            primaryTicker,
            StringComparison.OrdinalIgnoreCase
        );
        return splits
            .Where(split =>
                string.Equals(
                    split.PriceSeriesTicker,
                    listedTicker,
                    StringComparison.OrdinalIgnoreCase
                ) || (isPrimary && split.PriceSeriesTicker == null)
            )
            .ToList();
    }
}
