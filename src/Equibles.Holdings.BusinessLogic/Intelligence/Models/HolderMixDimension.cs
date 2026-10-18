using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>A way to split a stock's 13F holders into buckets for its holder mix.</summary>
public enum HolderMixDimension
{
    [Display(Name = "Holder type")]
    EntityType = 0,

    [Display(Name = "Style")]
    Style = 1,

    [Display(Name = "Turnover")]
    Turnover = 2,

    [Display(Name = "Holding horizon")]
    Horizon = 3,

    [Display(Name = "13F book size")]
    BookSize = 4,

    [Display(Name = "Client base")]
    ClientBase = 5,

    [Display(Name = "Private funds")]
    PrivateFunds = 6,

    [Display(Name = "Location")]
    Location = 7,
}
