using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A 13F filer's active share against one benchmark in one closed quarter, published with its
/// <see cref="HolderQuarterlyMetrics"/>. The benchmark is a published index read from a tracking
/// fund's holdings or the index's constituents, or every filer's combined book, as of
/// <see cref="BenchmarkAsOf"/>.
/// </summary>
[Index(nameof(InstitutionalHolderId), nameof(ReportDate), nameof(BenchmarkKey), IsUnique = true)]
[Index(nameof(ReportDate), nameof(BenchmarkKey))]
public class HolderQuarterlyBenchmarkActiveShare
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }
    public DateOnly ReportDate { get; set; }

    /// <summary>The benchmark's stable key, such as <c>sp-500</c> or <c>13f-aggregate</c>.</summary>
    [Required]
    [MaxLength(64)]
    public string BenchmarkKey { get; set; }

    /// <summary>The date the benchmark's holdings or constituents are as of.</summary>
    public DateOnly BenchmarkAsOf { get; set; }

    [Precision(12, 6)]
    public decimal ActiveShare { get; set; }

    /// <summary>The benchmark's weight in names the filer holds, from 0 to 1.</summary>
    [Precision(12, 6)]
    public decimal Overlap { get; set; }

    /// <summary>
    /// The benchmark's weight in names resolved to a tracked issuer, from 0 to 1. Active share and
    /// overlap are measured over the resolved names reweighted to sum to one, and a benchmark under
    /// the minimum coverage is never the closest one.
    /// </summary>
    [Precision(12, 6)]
    public decimal Coverage { get; set; }
}
