namespace Equibles.InsiderTrading.BusinessLogic.Models;

/// <summary>
/// Outcome of evaluating one InsiderTransaction's reported per-share price
/// against the market close. Mirrors the fields the caller persists onto the
/// entity.
/// </summary>
public class InsiderTransactionPriceEvaluation
{
    /// <summary>
    /// Resulting plausibility: <c>null</c> = pending (no usable close yet),
    /// <c>true</c> = valid or repaired, <c>false</c> = implausible and not
    /// repairable.
    /// </summary>
    public bool? IsPriceValid { get; set; }

    /// <summary>
    /// Per-share price to store: the reported value unchanged, or the
    /// repaired value (<c>reported / shares</c>) when a fat-finger was fixed.
    /// </summary>
    public decimal EffectivePrice { get; set; }

    /// <summary>True when <see cref="EffectivePrice"/> is a repaired value.</summary>
    public bool WasRepaired { get; set; }
}
