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
}
