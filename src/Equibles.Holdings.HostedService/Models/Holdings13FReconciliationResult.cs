using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Outcome of an on-demand 13F reconciliation run, returned to the Backoffice so
/// the click can be reported to the operator. Every run that examined a filer is
/// also persisted as a <see cref="HoldingsReconciliationLog"/> row
/// (<see cref="FilerExamined"/> = true). A "no candidates" response examines no
/// filer, so there is nothing to audit and no row is written.
/// </summary>
public class Holdings13FReconciliationResult
{
    /// <summary>
    /// True when a filer was actually checked against EDGAR (and thus logged);
    /// false when there was no lagging filer to reconcile.
    /// </summary>
    public bool FilerExamined { get; set; }

    public ReconciliationOutcome Outcome { get; set; }

    public Guid? HolderId { get; set; }

    public string HolderName { get; set; }

    public string HolderCik { get; set; }

    public int QuartersReingested { get; set; }

    /// <summary>Human-readable summary, suitable for a flash message.</summary>
    public string Message { get; set; }

    public static Holdings13FReconciliationResult NoCandidates(string message) =>
        new() { FilerExamined = false, Message = message };
}
