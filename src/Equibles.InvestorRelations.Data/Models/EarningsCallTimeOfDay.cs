using System.ComponentModel.DataAnnotations;

namespace Equibles.InvestorRelations.Data.Models;

public enum EarningsCallTimeOfDay
{
    [Display(Name = "Unspecified")]
    Unspecified,

    [Display(Name = "Before Market Open")]
    BeforeMarketOpen,

    [Display(Name = "After Market Close")]
    AfterMarketClose,
}
