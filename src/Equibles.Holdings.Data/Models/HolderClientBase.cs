using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The client type holding at least half of an adviser's regulatory assets (Form ADV Item 5.D),
/// with related types grouped.
/// </summary>
public enum HolderClientBase
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "Diversified")]
    Diversified = 1,

    [Display(Name = "Individuals and high net worth")]
    Individuals = 2,

    [Display(Name = "Pooled investment vehicles")]
    PooledInvestmentVehicles = 3,

    [Display(Name = "Registered funds and BDCs")]
    RegisteredFunds = 4,

    [Display(Name = "Pension and profit sharing plans")]
    PensionPlans = 5,

    [Display(Name = "Insurance companies")]
    InsuranceCompanies = 6,

    [Display(Name = "Charitable organizations")]
    CharitableOrganizations = 7,

    [Display(Name = "Governments and sovereign funds")]
    GovernmentAndSovereign = 8,

    [Display(Name = "Banks and thrifts")]
    BanksAndThrifts = 9,

    [Display(Name = "Corporations")]
    Corporations = 10,

    [Display(Name = "Other investment advisers")]
    OtherInvestmentAdvisers = 11,

    [Display(Name = "Other")]
    Other = 12,
}
