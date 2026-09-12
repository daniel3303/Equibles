using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Data.Models;

// Original unattributed price shape, used only by historical migration fixtures.
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
