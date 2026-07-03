using System.ComponentModel.DataAnnotations.Schema;

namespace Equibles.Holdings.Data.Models;

// Per-quarter materialisation of the AUM aggregates that power /holdings/stats
// and /holdings/trends. The live multi-distinct GROUP BY over
// InstitutionalHoldings (~5-15M rows/quarter * 100+ quarters) cannot finish
// inside the 30s Npgsql command timeout, so the worker rebuilds one row per
// quarter on each 13F import and the request path scans this small table in
// quarter order.
//
// DirtyAt is the coalescing signal between Filings13FImportedConsumer (stamps
// it only when currently null, preserving the wave's first-event timestamp)
// and AumSnapshotDrainWorker (claims — clears — the flag once the cooldown has
// elapsed, then rebuilds). The claim happens BEFORE the rebuild: an import
// landing mid-rebuild finds the flag null and re-dirties the row, so its
// signal triggers another rebuild instead of being lost. Holding a timestamp
// instead of a bool lets the claim use optimistic concurrency (clear only if
// DirtyAt still matches the value seen when the drain selected the row).
// The partial index on DirtyAt is declared in HoldingsModuleConfiguration —
// the attribute form can't express the WHERE filter.
public class AumQuarterlySnapshot
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public DateOnly ReportDate { get; set; }

    public long TotalValue { get; set; }

    public int FilerCount { get; set; }

    public int PositionCount { get; set; }

    public int StockCount { get; set; }

    public int FilingCount { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DirtyAt { get; set; }

    [NotMapped]
    public double AvgPositionsPerFiler => FilerCount > 0 ? (double)PositionCount / FilerCount : 0;
}
