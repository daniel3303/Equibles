using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>Form ADV Part 1A Item 7.A, the kinds of related persons an adviser has. Item (n) is bit n-1.</summary>
[Flags]
public enum FormAdvRelatedPerson
{
    None = 0,

    [Display(Name = "Broker-dealer")]
    BrokerDealer = 1 << 0,

    [Display(Name = "Other investment adviser")]
    InvestmentAdviser = 1 << 1,

    [Display(Name = "Registered municipal advisor")]
    MunicipalAdvisor = 1 << 2,

    [Display(Name = "Registered security-based swap dealer")]
    SecurityBasedSwapDealer = 1 << 3,

    [Display(Name = "Major security-based swap participant")]
    MajorSecurityBasedSwapParticipant = 1 << 4,

    [Display(Name = "Commodity pool operator or commodity trading advisor")]
    CommodityPoolOperatorOrTradingAdvisor = 1 << 5,

    [Display(Name = "Futures commission merchant")]
    FuturesCommissionMerchant = 1 << 6,

    [Display(Name = "Banking or thrift institution")]
    BankOrThrift = 1 << 7,

    [Display(Name = "Trust company")]
    TrustCompany = 1 << 8,

    [Display(Name = "Accountant or accounting firm")]
    AccountingFirm = 1 << 9,

    [Display(Name = "Lawyer or law firm")]
    LawFirm = 1 << 10,

    [Display(Name = "Insurance company or agency")]
    InsuranceCompanyOrAgency = 1 << 11,

    [Display(Name = "Pension consultant")]
    PensionConsultant = 1 << 12,

    [Display(Name = "Real estate broker or dealer")]
    RealEstateBrokerOrDealer = 1 << 13,

    [Display(Name = "Sponsor or syndicator of limited partnerships")]
    LimitedPartnershipSponsor = 1 << 14,

    [Display(Name = "Sponsor or general partner of pooled investment vehicles")]
    PooledVehicleSponsor = 1 << 15,
}
