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
public class CashDividend
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly ExDate { get; set; }

    public decimal AmountPerShare { get; set; }

    public CashDividendSource Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
