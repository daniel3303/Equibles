namespace Equibles.Finra.BusinessLogic.Models;

/// <summary>
/// The price-derived pieces of a stock's squeeze score, computed by
/// <see cref="ShortSqueezePriceFactorCalculator"/> from its recent daily bars.
/// </summary>
public class ShortSqueezePriceFactors
{
    /// <summary>
    /// How far the latest adjusted close sits above (positive) or below (negative)
    /// the trailing volume-weighted average price, as a fraction of that average.
    /// A proxy for the aggregate paper loss of open short positions — the deeper
    /// the price trades above the level where recent volume changed hands, the
    /// more of the short base is underwater. Null when the price history is too
    /// short to average meaningfully.
    /// </summary>
    public decimal? PriceAboveVwap { get; set; }

    /// <summary>
    /// True when the recent-window return is positive and statistically extreme
    /// versus the stock's own daily volatility — the price-spike catalyst.
    /// </summary>
    public bool HasPriceSpikeCatalyst { get; set; }

    /// <summary>
    /// True when recent dollar turnover runs a multiple of the stock's baseline
    /// turnover while the recent return is positive — the abnormal-volume catalyst.
    /// </summary>
    public bool HasVolumeSurgeCatalyst { get; set; }
}
