using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Data.Models;

/// <summary>
/// An as-reported cash dividend for a <see cref="CommonStock"/>.
/// <see cref="ExDate"/> is the ex-dividend date (the first trading day the
/// stock trades without the dividend) and <see cref="AmountPerShare"/> is the
/// declared cash amount per share. The (stock, ex-date) pair is unique — it is
/// the idempotency guard for the capture upsert.
/// </summary>
[Index(nameof(CommonStockId), nameof(ExDate), IsUnique = true)]
[Index(nameof(PriceAdjustmentAppliedTime))]
public class CashDividend
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly ExDate { get; set; }

    public decimal AmountPerShare { get; set; }

    public CashDividendSource Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The amount incorporated into the last full price-history reconciliation. Keeping the
    /// applied value makes a restatement by an older worker detectable even when that worker does
    /// not know to clear <see cref="PriceAdjustmentAppliedTime"/>.
    /// </summary>
    public decimal? PriceAdjustmentAppliedAmountPerShare { get; set; }

    /// <summary>
    /// Null while this dividend still requires a full provider-history reconciliation of the
    /// stock's current primary listed series.
    /// </summary>
    public DateTime? PriceAdjustmentAppliedTime { get; set; }
}
