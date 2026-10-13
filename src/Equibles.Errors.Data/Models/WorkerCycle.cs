using System.ComponentModel.DataAnnotations;

namespace Equibles.Errors.Data.Models;

/// <summary>
/// When a worker lane last completed a full cycle, by its display name. A lane that opts in
/// skips the cycle a restart would start while this stamp is younger than its sleep interval,
/// since deploys restart the worker far more often than most lanes sleep.
/// </summary>
public class WorkerCycle
{
    [Key]
    [MaxLength(128)]
    public string WorkerName { get; set; }

    public DateTimeOffset CompletedAt { get; set; }
}
