using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One audit row per on-demand 13F reconciliation run (a Backoffice "reconcile"
/// click). Records which filer was checked against EDGAR, what — if anything —
/// was re-ingested, and who triggered it.
///
/// The reconciliation is meant to be a no-op: the cross-type amendment fix stops
/// new gaps forming, so a healthy filer comes back
/// <see cref="ReconciliationOutcome.AlreadyCurrent"/>. A
/// <see cref="ReconciliationOutcome.Reconciled"/> row therefore flags a gap that
/// (re)formed, which makes this log the catch-net for a regression in the import
/// path.
///
/// It also acts as the cursor for the "reconcile next lagging filer" button: a
/// filer checked within the recheck window is skipped, so repeated clicks advance
/// through the backlog instead of re-hitting the largest laggard.
/// </summary>
[Index(nameof(CreationTime))]
[Index(nameof(InstitutionalHolderId))]
public class HoldingsReconciliationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    /// <summary>The filer reconciled (an <c>InstitutionalHolder.Id</c>).</summary>
    public Guid InstitutionalHolderId { get; set; }

    public string HolderName { get; set; }

    public string HolderCik { get; set; }

    public ReconciliationOutcome Outcome { get; set; }

    /// <summary>
    /// How many missing 13F-HR quarters were re-ingested. Zero unless
    /// <see cref="Outcome"/> is <see cref="ReconciliationOutcome.Reconciled"/>.
    /// </summary>
    public int QuartersReingested { get; set; }

    /// <summary>
    /// Human-readable summary of what happened — the quarter ends re-ingested, or
    /// the reason nothing was (e.g. "EDGAR lists no 13F-HR quarter we are missing").
    /// </summary>
    public string Details { get; set; }

    /// <summary>The Backoffice user who triggered the run.</summary>
    public string TriggeredBy { get; set; }
}
