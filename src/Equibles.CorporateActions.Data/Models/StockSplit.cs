using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Data.Models;

/// <summary>
/// An as-reported corporate-action stock split for a <see cref="CommonStock"/>.
/// The ratio is expressed as <see cref="Numerator"/>:<see cref="Denominator"/>
/// (e.g. 10:1 is a forward split, 1:12 is a reverse split).
/// <see cref="PriceAdjustmentAppliedTime"/> is the idempotency marker for the
/// price back-adjustment pass — a null value means historical prices have not
/// yet been reconciled for this split.
/// </summary>
[Index(nameof(CommonStockId), nameof(EffectiveDate), IsUnique = true)]
[Index(nameof(PriceAdjustmentAppliedTime))]
public class StockSplit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly EffectiveDate { get; set; }

    public decimal Numerator { get; set; }

    public decimal Denominator { get; set; }

    public StockSplitSource Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    public DateTime? PriceAdjustmentAppliedTime { get; set; }
}
