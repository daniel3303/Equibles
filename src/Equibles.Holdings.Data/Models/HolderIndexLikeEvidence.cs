using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>The measurement that placed a filer in <see cref="HolderStyleBand.IndexLike"/>.</summary>
public enum HolderIndexLikeEvidence
{
    [Display(Name = "None")]
    None = 0,

    /// <summary>Its book's active share against the closest benchmark is low.</summary>
    [Display(Name = "Active share")]
    ActiveShare = 1,

    /// <summary>Index series hold most of the registered fund assets it advises, by N-CEN.</summary>
    [Display(Name = "Index fund assets")]
    IndexFundAssets = 2,
}
