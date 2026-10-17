using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>Form ADV Part 1A Item 6.A, other businesses the adviser itself is engaged in. Item (n) is bit n-1.</summary>
[Flags]
public enum FormAdvOtherBusiness
{
    None = 0,

    [Display(Name = "Broker-dealer")]
    BrokerDealer = 1 << 0,

    [Display(Name = "Registered representative of a broker-dealer")]
    BrokerDealerRepresentative = 1 << 1,

    [Display(Name = "Commodity pool operator or commodity trading advisor")]
    CommodityPoolOperatorOrTradingAdvisor = 1 << 2,

    [Display(Name = "Futures commission merchant")]
    FuturesCommissionMerchant = 1 << 3,

    [Display(Name = "Real estate broker, dealer or agent")]
    RealEstateBrokerDealerOrAgent = 1 << 4,

    [Display(Name = "Insurance broker or agent")]
    InsuranceBrokerOrAgent = 1 << 5,

    [Display(Name = "Bank")]
    Bank = 1 << 6,

    [Display(Name = "Trust company")]
    TrustCompany = 1 << 7,

    [Display(Name = "Registered municipal advisor")]
    MunicipalAdvisor = 1 << 8,

    [Display(Name = "Registered security-based swap dealer")]
    SecurityBasedSwapDealer = 1 << 9,

    [Display(Name = "Major security-based swap participant")]
    MajorSecurityBasedSwapParticipant = 1 << 10,

    [Display(Name = "Accountant or accounting firm")]
    AccountingFirm = 1 << 11,

    [Display(Name = "Lawyer or law firm")]
    LawFirm = 1 << 12,

    [Display(Name = "Other financial product salesperson")]
    OtherFinancialProductSalesperson = 1 << 13,
}
