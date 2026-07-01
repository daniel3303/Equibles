using System.ComponentModel.DataAnnotations;

namespace Equibles.CorporateActions.Data.Models;

public enum StockSplitSource
{
    [Display(Name = "Yahoo")]
    Yahoo,

    [Display(Name = "Massive")]
    Massive,

    [Display(Name = "SEC Filing")]
    SecFiling,

    [Display(Name = "Manual")]
    Manual,
}
