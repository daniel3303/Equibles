using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// Which way a <see cref="FilingOtherManager"/> edge points between the filing manager and the
/// other manager it names. A 13F cover page and its summary page each carry an other-manager
/// list, and they mean opposite things.
/// </summary>
public enum OtherManagerDirection
{
    /// <summary>
    /// The summary page's list (<c>otherManagers2Info</c> / <c>OTHERMANAGER2.tsv</c>): managers
    /// whose positions this filing reports. Parent to subsidiary — the combination report a
    /// holding company files on behalf of its asset-management arms.
    /// </summary>
    [Display(Name = "Included in this report")]
    IncludedInReport = 0,

    /// <summary>
    /// The cover page's list (<c>otherManagersInfo</c> / <c>OTHERMANAGER.tsv</c>): managers who
    /// report this filer's positions for it. Subsidiary to parent — the opposite edge, filed by
    /// the child rather than the parent.
    /// </summary>
    [Display(Name = "Reports for this filer")]
    ReportsForFiler = 1,
}
