using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Data.Models;

/// <summary>
/// An as-reported corporate-action stock split for a <see cref="Issuer"/>.
/// The ratio is expressed as <see cref="Numerator"/>:<see cref="Denominator"/>
/// (e.g. 10:1 is a forward split, 1:12 is a reverse split).
/// <see cref="PriceAdjustmentAppliedTime"/> is the idempotency marker for the price
/// back-adjustment pass. The marker is applied only when its UTC date is after the effective
/// date; null or older legacy markers remain pending.
/// </summary>
[Index(nameof(PriceAdjustmentAppliedTime))]
public class StockSplit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("CommonStockId")]
    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }

    // Null retains historical evidence whose exact source listing cannot be proved.
    public Guid? EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    /// <summary>
    /// The exact listed ticker whose Yahoo series produced this issuer-level action. The price
    /// reconciliation lane uses it instead of whichever symbol is primary later. Null preserves
    /// rows captured before exact series attribution existed, including by the old worker during a
    /// rolling upgrade; those rows stay pending until a new exact Yahoo observation labels them.
    /// </summary>
    [MaxLength(32)]
    public string PriceSeriesTicker { get; set; }

    public DateOnly EffectiveDate { get; set; }

    public decimal Numerator { get; set; }

    public decimal Denominator { get; set; }

    public StockSplitSource Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    public DateTime? PriceAdjustmentAppliedTime { get; set; }

    /// <summary>
    /// True only when reconciliation ran on a UTC date after the effective date. Older workers
    /// could stamp announced future splits before the provider history incorporated them.
    /// </summary>
    public bool IsPriceAdjustmentApplied()
    {
        return PriceAdjustmentAppliedTime != null
            && DateOnly.FromDateTime(PriceAdjustmentAppliedTime.Value) > EffectiveDate;
    }
}
