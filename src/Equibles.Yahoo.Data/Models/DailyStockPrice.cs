using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Data.Models;

// Schema-only mapping for retiring binaries; current price paths use EquityDailyStockPrice.
public class DailyStockPrice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    /// <summary>
    /// The exact authoritative listed ticker this bar belongs to. This entity is stored in
    /// the isolated exact-listing table, so the value is always present for both primary and
    /// secondary listings and survives primary/secondary ordering changes.
    /// </summary>
    [Required, MaxLength(32)]
    public string ListedTicker { get; set; }

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

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
