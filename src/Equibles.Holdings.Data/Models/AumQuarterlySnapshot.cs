using System.ComponentModel.DataAnnotations.Schema;

namespace Equibles.Holdings.Data.Models;

// Per-quarter materialisation of the AUM aggregates that power /holdings/stats
// and /holdings/trends. The live multi-distinct GROUP BY over
// InstitutionalHoldings (~5-15M rows/quarter * 100+ quarters) cannot finish
// inside the 30s Npgsql command timeout, so the worker rebuilds one row per
// quarter on each 13F import and the request path scans this small table in
// quarter order.
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
}
