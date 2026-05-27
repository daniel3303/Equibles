using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// Per-worker progress marker for the real-time 13F daily-index sweep, keyed by
/// worker name. <see cref="SweptThrough"/> is the most recent date the worker
/// has fully swept; each cycle resumes from it (plus a short trailing
/// re-sweep), so the worker fetches only new/gap days instead of re-requesting
/// a whole quarter of daily indexes every run — which is what was triggering
/// SEC rate-limiting and stalling the sweep.
/// </summary>
[Index(nameof(WorkerName), IsUnique = true)]
public class RealtimeSweepState
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(128)]
    public string WorkerName { get; set; }

    public DateOnly SweptThrough { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
