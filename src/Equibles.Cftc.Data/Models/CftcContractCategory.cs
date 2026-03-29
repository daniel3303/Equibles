using System.ComponentModel.DataAnnotations;

namespace Equibles.Cftc.Data.Models;

public enum CftcContractCategory {
    [Display(Name = "Agriculture")]
    Agriculture,

    [Display(Name = "Energy")]
    Energy,

    [Display(Name = "Metals")]
    Metals,

    [Display(Name = "Equity Indices")]
    EquityIndices,

    [Display(Name = "Interest Rates")]
    InterestRates,

    [Display(Name = "Currencies")]
    Currencies,

    [Display(Name = "Other")]
    Other
}
