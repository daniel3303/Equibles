using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>What kind of institution a holder is, decided only from regulatory evidence.</summary>
public enum HolderEntityType
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "Registered investment adviser")]
    RegisteredAdviser = 1,

    [Display(Name = "Exempt reporting adviser")]
    ExemptReportingAdviser = 2,

    /// <summary>An adviser by its SIC code or a 13D/13G self-report but on neither Form ADV roster.</summary>
    [Display(Name = "Investment adviser")]
    InvestmentAdviser = 3,

    [Display(Name = "Bank")]
    Bank = 4,

    [Display(Name = "Insurance company")]
    InsuranceCompany = 5,

    [Display(Name = "Broker-dealer")]
    BrokerDealer = 6,

    [Display(Name = "Registered fund")]
    RegisteredFund = 7,

    [Display(Name = "Pension or benefit plan")]
    BenefitPlan = 8,

    [Display(Name = "Non-US institution")]
    NonUsInstitution = 9,

    /// <summary>
    /// A parent holding company or control person by its 13D/13G self-report (code HC), with no
    /// operating type (adviser, bank, insurer and so on) on its current records.
    /// </summary>
    [Display(Name = "Holding company")]
    HoldingCompany = 10,
}
