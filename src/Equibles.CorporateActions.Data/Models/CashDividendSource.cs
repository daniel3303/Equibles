using System.ComponentModel.DataAnnotations;

namespace Equibles.CorporateActions.Data.Models;

public enum CashDividendSource
{
    [Display(Name = "Yahoo")]
    Yahoo,

    [Display(Name = "Manual")]
    Manual,

    // A non-primary external data integration configured by the hosting deployment.
    [Display(Name = "External")]
    External,
}
