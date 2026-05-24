using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

public enum FundClassification
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "Bank")]
    Bank = 1,

    [Display(Name = "Insurance Company")]
    InsuranceCompany = 2,

    [Display(Name = "Pension Fund")]
    PensionFund = 3,

    [Display(Name = "Mutual Fund")]
    MutualFund = 4,

    [Display(Name = "Hedge Fund")]
    HedgeFund = 5,

    [Display(Name = "Private Equity")]
    PrivateEquity = 6,

    [Display(Name = "Venture Capital")]
    VentureCapital = 7,

    [Display(Name = "Endowment")]
    Endowment = 8,

    [Display(Name = "Sovereign Wealth Fund")]
    SovereignWealthFund = 9,

    [Display(Name = "Family Office")]
    FamilyOffice = 10,

    [Display(Name = "Broker-Dealer")]
    BrokerDealer = 11,
}
