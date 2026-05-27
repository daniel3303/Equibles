using System.ComponentModel.DataAnnotations.Schema;

namespace Equibles.Holdings.Data.Models;

// Per-quarter materialisation of the AUM aggregates that power /holdings/stats
// and /holdings/trends. The live multi-distinct GROUP BY over
// InstitutionalHoldings (~5-15M rows/quarter * 100+ quarters) cannot finish
// inside the 30s Npgsql command timeout, so the worker rebuilds one row per
// quarter on each 13F import and the request path scans this small table in
// quarter order.
//
// DirtyAt is the coalescing signal between Filings13FImportedConsumer (sets it
// on every import) and AumSnapshotDrainWorker (rebuilds the quarter once the
// cooldown has elapsed since the first event of a wave, then clears it).
// Holding the timestamp instead of a bool lets the drain worker clear the
// flag via optimistic concurrency (clear only if DirtyAt still matches the
// value seen at claim time), so an event that lands mid-rebuild isn't lost.
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
