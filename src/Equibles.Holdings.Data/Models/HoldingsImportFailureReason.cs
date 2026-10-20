using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

public enum HoldingsImportFailureReason
{
    [Display(Name = "Source could not be parsed")]
    UnreadableSource,

    [Display(Name = "Import incomplete")]
    Incomplete,

    [Display(Name = "Import failed")]
    ImportFailed,

    [Display(Name = "Stored identity conflict")]
    IdentityConflict,

    [Display(Name = "No tracked positions imported")]
    EmptyImport,

    [Display(Name = "Source position missing from storage")]
    MissingSourcePosition,

    [Display(Name = "Awaiting ordered filing recovery")]
    PendingRecovery,

    [Display(Name = "Report period is not a calendar quarter end")]
    OffQuarterReportPeriod,

    [Display(Name = "Filing withdrawn from EDGAR")]
    SourceWithdrawn,

    // Terminal reasons below close a row with nothing to store; the published source proves it.
    [Display(Name = "Source holds no tracked position")]
    NoTrackedPositions,

    [Display(Name = "Positions withheld under confidential treatment")]
    ConfidentialTreatment,

    [Display(Name = "Report period precedes the imported history")]
    OutsideHistoryWindow,
}
