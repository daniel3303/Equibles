using System.ComponentModel.DataAnnotations;

namespace Equibles.CorporateActions.Data.Models;

public enum CashDividendSource
{
    [Display(Name = "Yahoo")]
    Yahoo,

    [Display(Name = "Manual")]
    Manual,
}
