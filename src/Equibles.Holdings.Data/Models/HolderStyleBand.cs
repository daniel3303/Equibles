using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A 13F filer's portfolio style in one quarter, measured from its filed book; it states how the
/// book behaves, never what the firm calls itself.
/// </summary>
public enum HolderStyleBand
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "Index-like")]
    IndexLike = 1,

    [Display(Name = "Systematic-like")]
    SystematicLike = 2,

    [Display(Name = "Concentrated")]
    Concentrated = 3,

    [Display(Name = "Diversified active")]
    DiversifiedActive = 4,
}
