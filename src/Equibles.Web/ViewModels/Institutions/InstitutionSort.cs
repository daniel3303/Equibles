using System.ComponentModel.DataAnnotations;

namespace Equibles.Web.ViewModels.Institutions;

public enum InstitutionSort
{
    [Display(Name = "Name")]
    Name = 0,

    [Display(Name = "# Positions (latest 13F)")]
    PositionsDescending = 1,

    [Display(Name = "Total $ Value (latest 13F)")]
    ValueDescending = 2,

    [Display(Name = "3Y alpha vs S&P 500")]
    AlphaDescending = 3,
}
