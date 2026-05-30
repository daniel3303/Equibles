using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// The role a service provider plays for a registered investment company, as reported on a
/// Form N-CEN filing. A single filing names many providers across these categories.
/// </summary>
public enum NCenServiceProviderType
{
    [Display(Name = "Investment Adviser")]
    InvestmentAdviser,

    [Display(Name = "Sub-Adviser")]
    SubAdviser,

    [Display(Name = "Custodian")]
    Custodian,

    [Display(Name = "Transfer Agent")]
    TransferAgent,

    [Display(Name = "Administrator")]
    Administrator,

    [Display(Name = "Pricing Service")]
    PricingService,

    [Display(Name = "Shareholder Servicing Agent")]
    ShareholderServicingAgent,

    [Display(Name = "Principal Underwriter")]
    PrincipalUnderwriter,

    [Display(Name = "Independent Public Accountant")]
    PublicAccountant,
}
