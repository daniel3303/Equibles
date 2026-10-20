using System.ComponentModel.DataAnnotations;

namespace Equibles.Web.ViewModels.Stocks;

public enum PositionChangeType
{
    [Display(Name = "New")]
    New = 1,

    [Display(Name = "Increased")]
    Increased = 2,

    [Display(Name = "Reduced")]
    Reduced = 3,

    [Display(Name = "Unchanged")]
    Unchanged = 4,

    [Display(Name = "Sold out")]
    SoldOut = 5,

    // A current holder that filed no 13F for the prior quarter (its first 13F, or a return after
    // a gap): its position cannot be compared, so it is neither new nor unchanged.
    [Display(Name = "No prior 13F")]
    NoPriorFiling = 6,
}
