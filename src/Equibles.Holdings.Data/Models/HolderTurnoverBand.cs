using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>A 13F filer's trailing annual turnover, banded.</summary>
public enum HolderTurnoverBand
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "Low turnover")]
    Low = 1,

    [Display(Name = "Medium turnover")]
    Medium = 2,

    [Display(Name = "High turnover")]
    High = 3,
}
