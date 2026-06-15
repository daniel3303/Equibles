using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The result of reconciling one 13F filer against EDGAR's submission history,
/// recorded on each <see cref="HoldingsReconciliationLog"/> row. With the
/// cross-type amendment fix in place a healthy filer should reconcile to
/// <see cref="AlreadyCurrent"/>, so a <see cref="Reconciled"/> row is the signal
/// that a holdings gap (re)formed and is worth investigating.
/// </summary>
public enum ReconciliationOutcome
{
    /// <summary>EDGAR lists no 13F-HR quarter we are missing — nothing changed.</summary>
    [Display(Name = "Already current")]
    AlreadyCurrent,

    /// <summary>At least one missing 13F-HR quarter was re-ingested.</summary>
    [Display(Name = "Reconciled")]
    Reconciled,

    /// <summary>The EDGAR lookup or re-ingest failed; nothing was changed.</summary>
    [Display(Name = "Failed")]
    Failed,
}
