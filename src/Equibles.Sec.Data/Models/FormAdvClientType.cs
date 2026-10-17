using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>Form ADV Part 1A Item 5.D client types, in the form's letter order (a) to (n).</summary>
public enum FormAdvClientType
{
    [Display(Name = "Individuals (other than high net worth)")]
    Individuals = 0,

    [Display(Name = "High net worth individuals")]
    HighNetWorthIndividuals = 1,

    [Display(Name = "Banking or thrift institutions")]
    BanksOrThrifts = 2,

    [Display(Name = "Investment companies")]
    InvestmentCompanies = 3,

    [Display(Name = "Business development companies")]
    BusinessDevelopmentCompanies = 4,

    [Display(Name = "Pooled investment vehicles")]
    PooledInvestmentVehicles = 5,

    [Display(Name = "Pension and profit sharing plans")]
    PensionAndProfitSharingPlans = 6,

    [Display(Name = "Charitable organizations")]
    CharitableOrganizations = 7,

    [Display(Name = "State or municipal government entities")]
    GovernmentEntities = 8,

    [Display(Name = "Other investment advisers")]
    OtherInvestmentAdvisers = 9,

    [Display(Name = "Insurance companies")]
    InsuranceCompanies = 10,

    [Display(Name = "Sovereign wealth funds and foreign official institutions")]
    SovereignWealthFunds = 11,

    [Display(Name = "Corporations or other businesses")]
    Corporations = 12,

    [Display(Name = "Other")]
    Other = 13,
}
