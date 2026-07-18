using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

// Combined-lane twin of StockQuarterlyActivity, materialising the open filing window's
// carry-forward view: "current" figures aggregate the funds that already filed the open
// quarter PLUS every non-filer carried forward at its prior-quarter position, so the
// market-wide surfaces don't read half the universe as sellers while filings trickle in.
//
// A separate table rather than a lane column on StockQuarterlyActivity: the plain
// snapshot has many consumers (heat map, REST closed-quarter lane, screener, portal)
// whose queries must never see a combined row, and the combined lane's lifecycle is
// different — it only ever holds ONE quarter's rows (the currently open one) and is
// deleted outright when the 45-day window closes (CombinedQuarterHelper). Rebuilt by
// HoldingsAggregateRefreshService on the same DirtyAt-coalesced drain that maintains
// the plain snapshot, since every 13F arriving during the window dirties the open
// quarter. Consumers read this instead of running the live combined aggregation
// (GROUP BY over ~3.4M rows + correlated NOT-EXISTS probes, ~30s cold) per request.
[PrimaryKey(nameof(CommonStockId), nameof(ReportDate))]
[Index(nameof(ReportDate))]
public class StockQuarterlyActivityCombined
{
    public Guid CommonStockId { get; set; }

    public DateOnly ReportDate { get; set; }

    public DateOnly PreviousReportDate { get; set; }

    public long CurrentShares { get; set; }
    public long PreviousShares { get; set; }
    public long CurrentValue { get; set; }
    public long PreviousValue { get; set; }

    public int CurrentFilerCount { get; set; }
    public int PreviousFilerCount { get; set; }
    public int NewFilerCount { get; set; }
    public int SoldOutFilerCount { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
