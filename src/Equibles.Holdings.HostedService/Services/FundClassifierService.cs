using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

public static class FundClassifierService
{
    private static readonly (string Pattern, FundClassification Classification)[] Rules =
    [
        ("SOVEREIGN WEALTH", FundClassification.SovereignWealthFund),
        ("FAMILY OFFICE", FundClassification.FamilyOffice),
        ("VENTURE CAPITAL", FundClassification.VentureCapital),
        ("VENTURE PARTNERS", FundClassification.VentureCapital),
        ("PRIVATE EQUITY", FundClassification.PrivateEquity),
        ("BUYOUT", FundClassification.PrivateEquity),
        ("PENSION", FundClassification.PensionFund),
        ("RETIREMENT", FundClassification.PensionFund),
        ("SUPERANNUATION", FundClassification.PensionFund),
        ("ENDOWMENT", FundClassification.Endowment),
        ("UNIVERSITY", FundClassification.Endowment),
        ("FOUNDATION", FundClassification.Endowment),
        ("INSURANCE", FundClassification.InsuranceCompany),
        ("ASSURANCE", FundClassification.InsuranceCompany),
        ("REINSURANCE", FundClassification.InsuranceCompany),
        ("UNDERWRITER", FundClassification.InsuranceCompany),
        ("LIFE INS", FundClassification.InsuranceCompany),
        ("MUTUAL FUND", FundClassification.MutualFund),
        ("MUTUAL LIFE", FundClassification.InsuranceCompany),
        ("INDEX FUND", FundClassification.MutualFund),
        ("EXCHANGE TRADED FUND", FundClassification.MutualFund),
        ("ETF", FundClassification.MutualFund),
        ("HEDGE FUND", FundClassification.HedgeFund),
        ("BROKER", FundClassification.BrokerDealer),
        ("BROKERAGE", FundClassification.BrokerDealer),
        ("SECURITIES", FundClassification.BrokerDealer),
        ("BANCORP", FundClassification.Bank),
        ("BANCSHARES", FundClassification.Bank),
        ("BANKERS", FundClassification.Bank),
        ("NATIONAL BANK", FundClassification.Bank),
        ("STATE BANK", FundClassification.Bank),
        ("SAVINGS BANK", FundClassification.Bank),
        ("BANK OF", FundClassification.Bank),
        ("BANK ", FundClassification.Bank),
        ("TRUST CO", FundClassification.Bank),
    ];

    public static FundClassification Classify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FundClassification.Unknown;

        var upper = name.ToUpperInvariant() + " ";

        foreach (var (pattern, classification) in Rules)
        {
            if (upper.Contains(pattern))
                return classification;
        }

        return FundClassification.Unknown;
    }
}
