using System.ComponentModel.DataAnnotations;

namespace Equibles.CorporateActions.Data.Models;

public enum StockSplitSource
{
    // Persisted values also define capture precedence: Manual > SecFiling > External > Yahoo.
    // A lower-priority refresh must never undo a corrected action definition.
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
