using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The last fund-scoring visit of a filer under one (window, benchmark), whether or not it
/// produced a <see cref="FundScore"/>. A visit that yields no score leaves no score row, so
/// without this stamp every unscoreable filer was re-read on each cycle.
/// </summary>
[Index(
    nameof(InstitutionalHolderId),
    nameof(WindowYears),
    nameof(BenchmarkTicker),
    IsUnique = true
)]
public class FundScoreAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }

    [MaxLength(16)]
    public string BenchmarkTicker { get; set; }

    public int WindowYears { get; set; }

    /// <summary>The calculation version the visit ran under; an older one makes the filer due again.</summary>
    public int CalculationVersion { get; set; } = FundScore.CurrentCalculationVersion;

    public DateTime AttemptedAt { get; set; }
}
