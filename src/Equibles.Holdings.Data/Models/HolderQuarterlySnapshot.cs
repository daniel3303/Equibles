using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

// Per-(holder, quarter) materialisation of the Form 13F AUM aggregates behind
// the institutions browse ranking and the per-holder snapshot lookups. Deriving
// these live meant grouping each holder's entire InstitutionalHolding history
// per request (5-45s at production scale); concurrent traffic on the stock
// holdings pages saturated the database with copies of that query. This table
// holds one row per holder per quarter so those reads become indexed lookups.
//
// Rebuilt by HoldingsAggregateRefreshService.RebuildQuarter (drain worker), the
// same DirtyAt-coalesced path that maintains AumQuarterlySnapshot — when a
// quarter goes dirty its whole holder slice is recomputed and upserted. Only
// Form 13F rows feed the aggregates: Schedule 13D/G rows share the holdings
// table but carry event dates, and would otherwise hijack per-holder snapshots
// the same way they hijacked the global aggregates (GH-2556).
[PrimaryKey(nameof(InstitutionalHolderId), nameof(ReportDate))]
[Index(nameof(ReportDate))]
public class HolderQuarterlySnapshot
{
    public Guid InstitutionalHolderId { get; set; }

    public DateOnly ReportDate { get; set; }

    // Latest filing date among the quarter's 13F rows — amendments push it
    // forward past the original filing.
    public DateOnly FilingDate { get; set; }

    public long Aum { get; set; }

    // Raw row count for the quarter; one stock can contribute several rows
    // (share/option classes, manager splits).
    public int PositionCount { get; set; }

    // Distinct stocks held in the quarter.
    public int StockCount { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
