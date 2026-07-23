using System.ComponentModel.DataAnnotations;

namespace Equibles.GovernmentContracts.Data.Models;

/// <summary>
/// Resumable scan checkpoint for the government-contracts backfill, keyed by a
/// worker-assigned cursor name. Persists the end date of the last action-date window the
/// import fully completed, so a cycle aborted mid-scan by a transport failure resumes there
/// instead of restarting the whole range.
///
/// Decoupled from <c>MAX(GovernmentContract.ActionDate)</c> on purpose: that watermark only
/// advances when a window inserts rows, so an empty window (or one whose awards match no
/// public company) leaves it unchanged — and a window that keeps failing during one of
/// USAspending's intermittent bad spells then freezes the cursor and replays the same range
/// every cycle, flooding the error log. The checkpoint advances per fully-completed window
/// regardless of whether that window inserted anything.
/// </summary>
public class GovernmentContractsScanState
{
    [Key]
    [MaxLength(100)]
    public string Name { get; set; }

    /// <summary>
    /// Inclusive end date of the newest window the import fully completed; null until the
    /// first window lands.
    /// </summary>
    public DateOnly? LastCompletedWindowEnd { get; set; }

    /// <summary>When the checkpoint last advanced (UTC); null until the first advance.</summary>
    public DateTime? UpdatedAt { get; set; }
}
