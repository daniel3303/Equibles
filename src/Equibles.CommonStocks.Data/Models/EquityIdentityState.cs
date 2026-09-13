using System.ComponentModel.DataAnnotations;

namespace Equibles.CommonStocks.Data.Models;

public enum EquityIdentityState
{
    [Display(Name = "Legacy identity — venue unverified")]
    Legacy = 0,

    [Display(Name = "Verified listing")]
    Verified = 1,
}
