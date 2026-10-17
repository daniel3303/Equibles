using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The holder-intelligence drain's state for one closed 13F quarter. The 13F aggregate rebuild and
/// a quarter whose position presence changed stamp <see cref="DirtyAt"/>; the drain clears it only
/// when no newer stamp arrived while it rebuilt the quarter.
/// </summary>
[Index(nameof(ReportDate), IsUnique = true)]
public class HolderIntelligenceQuarterState
{
    // Client-generated, so FlexLabs UpsertRange sends it; a store-generated key inserts null.
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly ReportDate { get; set; }

    /// <summary>When the quarter's inputs last changed; null once its metrics are current.</summary>
    public DateTime? DirtyAt { get; set; }

    public DateTime? ComputedAt { get; set; }
    public int CalculationVersion { get; set; }
    public int FailureCount { get; set; }
    public string LastError { get; set; }
}
