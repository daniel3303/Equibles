using System.ComponentModel.DataAnnotations;

namespace Equibles.Web.ViewModels.ShortVolume;

public enum ShortVolumeSort
{
    [Display(Name = "Short Volume (high → low)")]
    ShortVolumeDescending = 0,

    [Display(Name = "Short % (high → low)")]
    ShortPercentDescending = 1,

    [Display(Name = "Total Volume (high → low)")]
    TotalVolumeDescending = 2,

    [Display(Name = "Ticker (A → Z)")]
    Ticker = 3,
}
