using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Data.Models;

// A settled daily bar belonging to one stable exchange listing.
[Index(nameof(EquityListingId), nameof(Date), IsUnique = true)]
[Index(nameof(Date))]
public class EquityDailyStockPrice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquityListingId { get; set; }
    public virtual EquityListing Listing { get; set; }

    // Original stored series spelling; retained even when a listing is subsequently renamed.
    [MaxLength(32)]
    public string SourceTicker { get; set; }

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
