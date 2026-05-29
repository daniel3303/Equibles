using System.ComponentModel.DataAnnotations;

namespace Equibles.Web.ViewModels.ShortActivity;

public enum MostShortedSort
{
    [Display(Name = "Current Short Position (high → low)")]
    CurrentShortPositionDescending = 0,

    [Display(Name = "Change vs Previous (high → low)")]
    ChangeDescending = 1,

    [Display(Name = "Days to Cover (high → low)")]
    DaysToCoverDescending = 2,

    [Display(Name = "Ticker (A → Z)")]
    Ticker = 3,
}
