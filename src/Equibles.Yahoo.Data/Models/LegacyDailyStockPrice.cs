using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Data.Models;

/// <summary>
/// Schema-only compatibility mapping for the pre-listing price table. Current price paths must
/// use <see cref="DailyStockPrice"/>; retaining this model prevents future migrations from
/// treating the untouched legacy table as orphaned storage.
/// </summary>
public class LegacyDailyStockPrice
{
    public Guid Id { get; set; }

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly Date { get; set; }

    [Precision(18, 4)]
    public decimal Open { get; set; }

    [Precision(18, 4)]
    public decimal High { get; set; }

    [Precision(18, 4)]
    public decimal Low { get; set; }

    [Precision(18, 4)]
    public decimal Close { get; set; }

    [Precision(18, 4)]
    public decimal AdjustedClose { get; set; }

    public long Volume { get; set; }

    public DateTime CreationTime { get; set; }
}
