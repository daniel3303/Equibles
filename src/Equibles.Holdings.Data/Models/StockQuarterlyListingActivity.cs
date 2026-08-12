using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

// Exact listing-series share totals that accompany StockQuarterlyActivity. The stock-level
// snapshot deliberately collapses every share class for ranking and filer counts, but a split
// attributed to one class must not restate its siblings. Keeping this small breakdown lets
// request surfaces apply each series' captured splits without returning to the holdings corpus.
[PrimaryKey(
    nameof(CommonStockId),
    nameof(ReportDate),
    nameof(IsCombined),
    nameof(PriceSeriesTicker)
)]
[Index(nameof(ReportDate), nameof(IsCombined))]
public class StockQuarterlyListingActivity
{
    public Guid CommonStockId { get; set; }

    public DateOnly ReportDate { get; set; }

    public bool IsCombined { get; set; }

    // Always non-null: a holding's legacy null ListedTicker is materialized as the stock's
    // authoritative primary ticker, while explicit sibling rows retain their exact ticker.
    [MaxLength(32)]
    public string PriceSeriesTicker { get; set; }

    public long CurrentShares { get; set; }

    public long PreviousShares { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
