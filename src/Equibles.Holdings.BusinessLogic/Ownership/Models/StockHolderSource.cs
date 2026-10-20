using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>The disclosure a unified holder-list row comes from.</summary>
public enum StockHolderSource
{
    [Display(Name = "13F")]
    Form13F = 0,

    [Display(Name = "13D")]
    Schedule13D = 1,

    [Display(Name = "13G")]
    Schedule13G = 2,

    [Display(Name = "Insider")]
    Insider = 3,

    [Display(Name = "Fund")]
    Fund = 4,

    [Display(Name = "Non-US")]
    NonUs = 5,
}
