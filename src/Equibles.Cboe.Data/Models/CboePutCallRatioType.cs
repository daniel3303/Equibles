using System.ComponentModel.DataAnnotations;

namespace Equibles.Cboe.Data.Models;

public enum CboePutCallRatioType {
    [Display(Name = "Total Exchange")]
    Total,

    [Display(Name = "Equity")]
    Equity,

    [Display(Name = "Index")]
    Index,

    [Display(Name = "VIX")]
    Vix,

    [Display(Name = "ETP")]
    Etp
}
