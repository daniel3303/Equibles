using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// Form ADV Part 1A Item 2.A, why an adviser may register with the SEC. Item (n) is bit n-1;
/// item (3) is reserved on the form.
/// </summary>
[Flags]
public enum FormAdvRegistrationBasis
{
    None = 0,

    [Display(Name = "Large advisory firm")]
    LargeAdvisoryFirm = 1 << 0,

    [Display(Name = "Mid-sized advisory firm")]
    MidSizedAdvisoryFirm = 1 << 1,

    [Display(Name = "Principal office outside the United States")]
    PrincipalOfficeOutsideUnitedStates = 1 << 3,

    [Display(Name = "Adviser to a registered investment company")]
    RegisteredInvestmentCompanyAdviser = 1 << 4,

    [Display(Name = "Adviser to a business development company")]
    BusinessDevelopmentCompanyAdviser = 1 << 5,

    [Display(Name = "Pension consultant")]
    PensionConsultant = 1 << 6,

    [Display(Name = "Related adviser")]
    RelatedAdviser = 1 << 7,

    [Display(Name = "Expects SEC eligibility within 120 days")]
    ExpectsEligibilityWithin120Days = 1 << 8,

    [Display(Name = "Multi-state adviser")]
    MultiStateAdviser = 1 << 9,

    [Display(Name = "Internet adviser")]
    InternetAdviser = 1 << 10,

    [Display(Name = "SEC exemptive order")]
    SecExemptiveOrder = 1 << 11,

    [Display(Name = "No longer eligible for SEC registration")]
    NoLongerEligible = 1 << 12,
}
