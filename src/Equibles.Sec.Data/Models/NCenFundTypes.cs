using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>Form N-CEN Item C.3 fund types a fund reports; none means it reported "N/A".</summary>
[Flags]
public enum NCenFundTypes
{
    [Display(Name = "None")]
    None = 0,

    [Display(Name = "Exchange-Traded Fund")]
    ExchangeTradedFund = 1 << 0,

    [Display(Name = "Exchange-Traded Managed Fund")]
    ExchangeTradedManagedFund = 1 << 1,

    [Display(Name = "Index Fund")]
    IndexFund = 1 << 2,

    /// <summary>Seeks a multiple or the inverse of an index's return.</summary>
    [Display(Name = "Multiple or Inverse Index Fund")]
    MultipleOrInverseIndexFund = 1 << 3,

    [Display(Name = "Interval Fund")]
    IntervalFund = 1 << 4,

    [Display(Name = "Fund of Funds")]
    FundOfFunds = 1 << 5,

    [Display(Name = "Master-Feeder Fund")]
    MasterFeederFund = 1 << 6,

    [Display(Name = "Money Market Fund")]
    MoneyMarketFund = 1 << 7,

    [Display(Name = "Target Date Fund")]
    TargetDateFund = 1 << 8,

    /// <summary>Underlying fund of a registered insurance company separate account.</summary>
    [Display(Name = "Underlying Fund")]
    UnderlyingFund = 1 << 9,
}
