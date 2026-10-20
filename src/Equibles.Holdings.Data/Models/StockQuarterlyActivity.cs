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
[PrimaryKey(nameof(EquityIssuerId), nameof(ReportDate))]
[Index(nameof(ReportDate))]
public class StockQuarterlyActivity
{
    public Guid EquityIssuerId { get; set; }

    public DateOnly ReportDate { get; set; }

    public DateOnly? PreviousReportDate { get; set; }

    public long CurrentShares { get; set; }
    public long PreviousShares { get; set; }
    public long CurrentValue { get; set; }
    public long PreviousValue { get; set; }

    // The part of each total held by filers without a 13F for the other quarter: current
    // positions of a filer with no prior-quarter 13F (a first 13F, or a return after a gap) and
    // prior positions of a filer with no 13F this quarter (not yet filed, or withheld under
    // confidential treatment). A missing filing is no trade, so the buy and sell movers compare
    // the totals without them. Zero on a row written before these columns existed.
    public long UnmatchedCurrentShares { get; set; }
    public long UnmatchedPreviousShares { get; set; }
    public long UnmatchedCurrentValue { get; set; }
    public long UnmatchedPreviousValue { get; set; }

    public int CurrentFilerCount { get; set; }
    public int PreviousFilerCount { get; set; }
    public int NewFilerCount { get; set; }
    public int SoldOutFilerCount { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public IReadOnlyList<StockQuarterlyListingActivity> ListingShares { get; set; } = [];
}
