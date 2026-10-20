using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The kind of registered fund a series is, from its Form N-CEN Item C.3 fund types and Item E
/// exchange listings, never from its name.
/// </summary>
public enum FundOwnershipBucket
{
    /// <summary>Reports itself an index fund and is not exchange-traded.</summary>
    [Display(Name = "Index funds")]
    IndexFund = 0,

    /// <summary>Reports itself an index fund and an exchange-traded fund.</summary>
    [Display(Name = "Index ETFs")]
    IndexEtf = 1,

    /// <summary>Exchange-traded without reporting itself an index fund.</summary>
    [Display(Name = "Active ETFs")]
    ActiveEtf = 2,

    /// <summary>Files an N-CEN fund report that marks it neither an index fund nor exchange-traded.</summary>
    [Display(Name = "Active funds")]
    ActiveFund = 3,

    /// <summary>No N-CEN fund report on or before the quarter end identifies the series.</summary>
    [Display(Name = "Unclassified")]
    Unclassified = 4,
}
