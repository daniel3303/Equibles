using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

// Per-(quarter, sector) materialisation of the sector-allocation aggregates
// that power /holdings/trends. Same rebuild path as AumQuarterlySnapshot; the
// composite key keeps the table compact (one row per sector per quarter).
public class SectorQuarterlySnapshot
{
    public DateOnly ReportDate { get; set; }

    public Guid SectorId { get; set; }

    [MaxLength(128)]
    public string SectorName { get; set; }

    public long TotalValue { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
