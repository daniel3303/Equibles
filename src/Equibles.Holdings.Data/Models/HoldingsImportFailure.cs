using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>A durable retry and audit record; absence from a trailing sweep never resolves it.</summary>
[Index(nameof(ResolvedAt), nameof(NextAttemptAt))]
public class HoldingsImportFailure
{
    [Key, MaxLength(32)]
    public string AccessionNumber { get; set; }

    [Required, MaxLength(20)]
    public string Cik { get; set; }
    public DateOnly FilingDate { get; set; }
    public DateOnly? ReportDate { get; set; }
    public HoldingsImportFailureReason Reason { get; set; }
    public int Attempts { get; set; }
    public DateTime FirstFailedAt { get; set; }
    public DateTime LastAttemptAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
