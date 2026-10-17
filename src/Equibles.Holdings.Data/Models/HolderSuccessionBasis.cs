using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>The filed evidence that ties a predecessor's 13F reports to its successor.</summary>
[Flags]
public enum HolderSuccessionBasis
{
    None = 0,

    /// <summary>A later 13F notice from the predecessor names the successor as reporting for it.</summary>
    [Display(Name = "Named in the predecessor's 13F notice")]
    NoticeReportedBy = 1 << 0,

    /// <summary>The predecessor's last 13F report included the successor's holdings.</summary>
    [Display(Name = "Included in the predecessor's last 13F")]
    IncludedInLastReport = 1 << 1,

    /// <summary>The successor's first 13F report includes the predecessor's holdings.</summary>
    [Display(Name = "Predecessor included in the successor's first 13F")]
    SuccessorIncludesPredecessor = 1 << 2,

    /// <summary>
    /// Every manager the predecessor's last 13F included is included again in the successor's
    /// first 13F (at least three), as when a holding company re-registers under a new CIK.
    /// </summary>
    [Display(Name = "Same included managers")]
    SameIncludedManagers = 1 << 3,

    /// <summary>Both filers carry the same CRD, 13F file number or SEC file number.</summary>
    [Display(Name = "Same identity number")]
    SameIdentityNumber = 1 << 4,
}
