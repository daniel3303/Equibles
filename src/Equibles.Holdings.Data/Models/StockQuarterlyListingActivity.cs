using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

// Exact listing-series share totals that accompany StockQuarterlyActivity. The stock-level
// snapshot deliberately collapses every share class for ranking and filer counts, but a split
// attributed to one class must not restate its siblings. Keeping this small breakdown lets
// request surfaces apply each series' captured splits without returning to the holdings corpus.
[PrimaryKey(
    nameof(EquityIssuerId),
    nameof(ReportDate),
    nameof(IsCombined),
    nameof(PriceSeriesTicker)
)]
[Index(nameof(ReportDate), nameof(IsCombined))]
public class StockQuarterlyListingActivity
{
    public Guid EquityIssuerId { get; set; }

    public DateOnly ReportDate { get; set; }

    public bool IsCombined { get; set; }

    // Always non-null: a holding's legacy null ListedTicker is materialized as the stock's
    // authoritative primary ticker, while explicit sibling rows retain their exact ticker.
    [MaxLength(32)]
    public string PriceSeriesTicker { get; set; }

    public long CurrentShares { get; set; }

    public long PreviousShares { get; set; }

    // Current-quarter figures over this exact listing's 13F rows, so exact-listing request
    // reads (ETF and sibling-class pages) never group the holdings corpus. Null means the row
    // predates these columns or belongs to the combined lane; readers then fall back to the
    // live aggregate. A rebuilt closed-quarter row always carries values, zero when sold out.
    public long? CurrentValue { get; set; }

    // Distinct 13F filers with a row in this listing-quarter.
    public int? CurrentFilerCount { get; set; }

    // Concentration numerators over each filer's summed value: HHI is
    // 10,000 * HolderValueSquaredSum / CurrentValue^2 and top-k shares are TopKValue / CurrentValue.
    public double? HolderValueSquaredSum { get; set; }

    public long? TopOneValue { get; set; }

    public long? TopFiveValue { get; set; }

    public long? TopTenValue { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
