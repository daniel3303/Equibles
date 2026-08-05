namespace Equibles.InsiderTrading.BusinessLogic.Models;

/// <summary>
/// The stored daily bar a reported insider price is checked against, plus the split basis
/// linking the two.
/// </summary>
/// <remarks>
/// The stored series is rewritten onto TODAY'S post-split basis by the split reconciliation,
/// while the filer's price is quoted on the TRANSACTION DATE's basis. The two differ by
/// <see cref="SplitFactorToPresent"/>: <c>Close × SplitFactorToPresent</c> is the close
/// restated onto the as-filed basis. Validation must accept a price plausible on EITHER basis
/// — treating the stored close as unadjusted once "repaired" 15,822 correct pre-split prices
/// into nonsense (AMZN's pre-20:1 $3,300.24 became $8.42).
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
    /// Product of the split ratios between the transaction date and the stored series' present
    /// basis; 1 when no split intervened. Multiplying a stored figure by this lands it on the
    /// as-filed basis.
    /// </summary>
    public decimal SplitFactorToPresent { get; set; } = 1m;

    /// <summary>
    /// True when the factor could not be established (a captured split's price adjustment has
    /// not run yet, or a split's series attribution is unknown) — the stored series straddles
    /// two bases and no honest verdict exists, so evaluation stays pending.
    /// </summary>
    public bool SplitBasisAmbiguous { get; set; }
}
