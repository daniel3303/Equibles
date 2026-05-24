using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class FundClassifierServiceTests
{
    [Theory]
    [InlineData("JPMORGAN CHASE BANK NA", FundClassification.Bank)]
    [InlineData("BANK OF AMERICA CORP", FundClassification.Bank)]
    [InlineData("FIRST BANCSHARES INC", FundClassification.Bank)]
    [InlineData("ZIONS BANCORP NA", FundClassification.Bank)]
    [InlineData("NORTHERN TRUST CO", FundClassification.Bank)]
    [InlineData("PEOPLES SAVINGS BANK", FundClassification.Bank)]
    [InlineData("NATIONAL BANK OF CANADA", FundClassification.Bank)]
    [InlineData("STATE BANK OF INDIA", FundClassification.Bank)]
    public void Classify_BankNames_ReturnsBank(string name, FundClassification expected)
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("METLIFE INSURANCE CO", FundClassification.InsuranceCompany)]
    [InlineData("PRUDENTIAL INSURANCE CO", FundClassification.InsuranceCompany)]
    [InlineData("SWISS REINSURANCE CO", FundClassification.InsuranceCompany)]
    [InlineData("GREAT WEST LIFE ASSURANCE", FundClassification.InsuranceCompany)]
    [InlineData("NEW YORK LIFE INS CO", FundClassification.InsuranceCompany)]
    public void Classify_InsuranceNames_ReturnsInsuranceCompany(
        string name,
        FundClassification expected
    )
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("CALIFORNIA PUBLIC EMPLOYEES RETIREMENT SYSTEM", FundClassification.PensionFund)]
    [InlineData("ONTARIO TEACHERS PENSION PLAN", FundClassification.PensionFund)]
    [InlineData("AUSTRALIAN SUPERANNUATION FUND", FundClassification.PensionFund)]
    public void Classify_PensionNames_ReturnsPensionFund(string name, FundClassification expected)
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("VANGUARD INDEX FUND", FundClassification.MutualFund)]
    [InlineData("SPDR S&P 500 ETF", FundClassification.MutualFund)]
    [InlineData("FIDELITY MUTUAL FUND", FundClassification.MutualFund)]
    public void Classify_MutualFundNames_ReturnsMutualFund(string name, FundClassification expected)
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("CITADEL HEDGE FUND LP", FundClassification.HedgeFund)]
    public void Classify_HedgeFundNames_ReturnsHedgeFund(string name, FundClassification expected)
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("BLACKSTONE PRIVATE EQUITY PARTNERS", FundClassification.PrivateEquity)]
    [InlineData("KKR BUYOUT FUND", FundClassification.PrivateEquity)]
    public void Classify_PrivateEquityNames_ReturnsPrivateEquity(
        string name,
        FundClassification expected
    )
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("SEQUOIA VENTURE CAPITAL", FundClassification.VentureCapital)]
    [InlineData("KLEINER PERKINS VENTURE PARTNERS", FundClassification.VentureCapital)]
    public void Classify_VentureCapitalNames_ReturnsVentureCapital(
        string name,
        FundClassification expected
    )
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("HARVARD UNIVERSITY ENDOWMENT", FundClassification.Endowment)]
    [InlineData("YALE ENDOWMENT FUND", FundClassification.Endowment)]
    [InlineData("BILL AND MELINDA GATES FOUNDATION", FundClassification.Endowment)]
    [InlineData("STANFORD UNIVERSITY", FundClassification.Endowment)]
    public void Classify_EndowmentNames_ReturnsEndowment(string name, FundClassification expected)
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("NORWAY SOVEREIGN WEALTH FUND", FundClassification.SovereignWealthFund)]
    public void Classify_SovereignWealthFundNames_ReturnsSovereignWealthFund(
        string name,
        FundClassification expected
    )
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("SOROS FAMILY OFFICE", FundClassification.FamilyOffice)]
    public void Classify_FamilyOfficeNames_ReturnsFamilyOffice(
        string name,
        FundClassification expected
    )
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("GOLDMAN SACHS SECURITIES LLC", FundClassification.BrokerDealer)]
    [InlineData("INTERACTIVE BROKERS GROUP", FundClassification.BrokerDealer)]
    [InlineData("TD AMERITRADE BROKERAGE", FundClassification.BrokerDealer)]
    public void Classify_BrokerDealerNames_ReturnsBrokerDealer(
        string name,
        FundClassification expected
    )
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("BERKSHIRE HATHAWAY INC", FundClassification.Unknown)]
    [InlineData("VANGUARD GROUP INC", FundClassification.Unknown)]
    [InlineData("BLACKROCK INC", FundClassification.Unknown)]
    public void Classify_GenericAssetManagers_ReturnsUnknown(
        string name,
        FundClassification expected
    )
    {
        FundClassifierService.Classify(name).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NullOrEmptyName_ReturnsUnknown(string name)
    {
        FundClassifierService.Classify(name).Should().Be(FundClassification.Unknown);
    }

    [Fact]
    public void Classify_IsCaseInsensitive()
    {
        FundClassifierService
            .Classify("jpmorgan chase bank na")
            .Should()
            .Be(FundClassification.Bank);
    }

    [Fact]
    public void Classify_MutualLifeInsurance_MatchesInsuranceNotMutualFund()
    {
        FundClassifierService
            .Classify("MUTUAL LIFE INSURANCE CO")
            .Should()
            .Be(FundClassification.InsuranceCompany);
    }
}
