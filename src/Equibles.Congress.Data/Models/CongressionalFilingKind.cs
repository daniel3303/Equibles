using System.ComponentModel.DataAnnotations;

namespace Equibles.Congress.Data.Models;

public enum CongressionalFilingKind
{
    [Display(Name = "House Periodic Transaction Report")]
    HousePeriodicTransactionReport = 0,

    [Display(Name = "House Annual Report")]
    HouseAnnualReport = 1,

    [Display(Name = "Senate Periodic Transaction Report")]
    SenatePeriodicTransactionReport = 2,

    [Display(Name = "Senate Annual Report")]
    SenateAnnualReport = 3,
}
