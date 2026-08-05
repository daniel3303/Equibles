using System.ComponentModel.DataAnnotations;

namespace Equibles.CorporateActions.Data.Models;

public enum StockSplitSource
{
    [Display(Name = "Yahoo")]
    Yahoo,

    // A non-primary external data integration configured by the hosting deployment.
    [Display(Name = "External")]
    External,

    [Display(Name = "SEC Filing")]
    SecFiling,

    [Display(Name = "Manual")]
    Manual,
}
