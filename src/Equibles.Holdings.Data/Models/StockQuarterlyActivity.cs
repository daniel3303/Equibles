using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

// Per-(stock, quarter) materialisation of the cross-sectional 13F activity that
// powers /holdings/conviction-heat-map. Deriving these live meant scanning two
// quarters of InstitutionalHolding (~3.4M rows) plus ~3.4M correlated NOT-EXISTS
// probes for the new/sold-out filer counts — ~30s on a cache miss, holding a
// pooled connection the whole time (#1262). This table holds the same numbers
// (one row per stock per quarter) so the heat map reads ~6k pre-aggregated rows
// in quarter order instead.
//
// Rebuilt by HoldingsAggregateRefreshService.RebuildQuarter (drain worker), the
// same DirtyAt-coalesced path that maintains AumQuarterlySnapshot — when a
// quarter goes dirty its whole stock slice is recomputed against the prior
// quarter and upserted. PreviousReportDate is null for the earliest quarter on
// record (every filer then counts as new).
[PrimaryKey(nameof(CommonStockId), nameof(ReportDate))]
[Index(nameof(ReportDate))]
public class StockQuarterlyActivity
{
    public Guid CommonStockId { get; set; }

    public DateOnly ReportDate { get; set; }

    public DateOnly? PreviousReportDate { get; set; }

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
