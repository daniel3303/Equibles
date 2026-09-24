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
}
