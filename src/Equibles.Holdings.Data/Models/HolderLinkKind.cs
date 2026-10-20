using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// What a <see cref="HolderLinkEvidence"/> row says about two filers. The 13F kinds mean "reports
/// for", never "owns"; only the GLEIF kinds state ownership.
/// </summary>
public enum HolderLinkKind
{
    /// <summary>The parent's 13F summary page lists the child among the managers it includes.</summary>
    [Display(Name = "Included in the parent's 13F")]
    IncludedInParentReport = 0,

    /// <summary>The child's own 13F cover lists the parent as reporting part of its holdings.</summary>
    [Display(Name = "Child's 13F cover names the parent as reporting for it")]
    ChildCoverReportsBy = 1,

    /// <summary>The child's 13F notice names the parent as reporting its holdings.</summary>
    [Display(Name = "Child's 13F notice names the parent as reporting for it")]
    NoticeReportedBy = 2,

    /// <summary>GLEIF records the parent as the child's direct accounting parent.</summary>
    [Display(Name = "GLEIF direct parent")]
    GleifDirectParent = 3,

    /// <summary>GLEIF records the parent as the child's ultimate accounting parent.</summary>
    [Display(Name = "GLEIF ultimate parent")]
    GleifUltimateParent = 4,
}
