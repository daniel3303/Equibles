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
}
