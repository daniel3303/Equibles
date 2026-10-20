using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// Which evidence chose a <see cref="HolderRollup"/> filer's own parent, in precedence order; the
/// chain above it follows each parent's own choice.
/// </summary>
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
