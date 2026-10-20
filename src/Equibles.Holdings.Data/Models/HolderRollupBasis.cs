using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>Which evidence decided a <see cref="HolderRollup"/>'s ultimate parent, in precedence order.</summary>
public enum HolderRollupBasis
{
    [Display(Name = "No parent")]
    Self = 0,

    [Display(Name = "13F reporting chain")]
    ReportingParent = 1,

    [Display(Name = "GLEIF direct-parent chain")]
    GleifDirectParent = 2,

    [Display(Name = "GLEIF ultimate parent")]
    GleifUltimateParent = 3,
}
