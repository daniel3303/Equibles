using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>How long a 13F filer has held its book, by value-weighted tenure, banded.</summary>
public enum HolderHorizonBand
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "Short horizon")]
    Short = 1,

    [Display(Name = "Medium horizon")]
    Medium = 2,

    [Display(Name = "Long horizon")]
    Long = 3,
}
