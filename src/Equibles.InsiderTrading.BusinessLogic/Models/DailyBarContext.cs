namespace Equibles.InsiderTrading.BusinessLogic.Models;

/// <summary>
/// The stored daily bar a reported insider price is checked against, plus the split basis
/// linking the two.
/// </summary>
/// <remarks>
/// The stored raw series has no basis metadata, while the filer's price is quoted on the
/// transaction-date basis. <see cref="SplitFactorToPresent"/> is the captured split product, not
/// proof of the stored close's basis. Validation treats <c>Close × SplitFactorToPresent</c> as an
/// alternate comparison candidate; the missing raw-price basis signal is a separate lane defect.
/// </remarks>
public class DailyBarContext
{
    /// <summary>Stored close on (or the most recent session before) the transaction date.</summary>
    public decimal? Close { get; set; }

    /// <summary>Session low of the same bar; null when the bar carries no usable range.</summary>
    public decimal? Low { get; set; }

    /// <summary>Session high of the same bar; null when the bar carries no usable range.</summary>
    public decimal? High { get; set; }

    /// <summary>
    /// Product of captured split ratios since the transaction date; 1 when none intervened. It
    /// does not establish the stored raw bar's basis.
    /// </summary>
    public decimal SplitFactorToPresent { get; set; } = 1m;

    /// <summary>
    /// True when the factor could not be established (a captured split's reconciliation has not
    /// run yet, or a split's series attribution is unknown), so evaluation stays pending.
    /// </summary>
    public bool SplitBasisAmbiguous { get; set; }
}
