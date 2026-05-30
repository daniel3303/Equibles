using System.ComponentModel.DataAnnotations;

namespace Equibles.Yahoo.Repositories;

/// <summary>
/// The direction of a run of consecutive daily closes that each moved the same
/// way relative to the prior close.
/// </summary>
public enum PriceStreakDirection
{
    [Display(Name = "None")]
    None,

    [Display(Name = "Up")]
    Up,

    [Display(Name = "Down")]
    Down,
}
