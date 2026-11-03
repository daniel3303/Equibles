using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data.Models;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.Gleif.Models;

namespace Equibles.UnitTests.EquityMarkets;

// A UCITS ETF is a sub-fund: GLEIF maps its ISIN to the umbrella company while FIRDS names the fund itself. The two
// agree only when GLEIF places that fund under that umbrella, and the fund then issues the listing (SXR8's identities).
public class EquityMarketDirectoryImporterSubFundTests
{
    private const string Isin = "IE00B5BMR087";
    private const string FundLei = "5493006ESS9BM09FB892";
    private const string UmbrellaLei = "549300Q7FFITMZ2PFZ28";
    private const string RowUrl =
        "https://www.cashmarket.deutsche-boerse.com/cash-en/trading/Tradable-Instruments-Xetra#IE00B5BMR087-XETR";

    private static EquityMarket Xetra => EquityMarketCatalog.TryGet("xetra");

    private static EquityMarketDirectoryRow Row() =>
        new()
        {
            Isin = Isin,
            MarketIdentifierCode = "XETR",
            Symbol = "SXR8",
            Name = "ISHSVII-CORE S+P500 DL ACC",
            ReportedCurrency = "EUR",
            SourceUrl = new Uri(RowUrl),
        };

    private static EquityMarketDirectoryProduct Product() =>
        new()
        {
            SourceIssuerIdentifier = FundLei,
            Name = "ISHSVII-CORE S+P500 DL ACC",
            SourceUrl = new Uri(RowUrl),
            ReportedCurrency = "EUR",
            Evidence = new { Isin },
        };

    private static FirdsInstrumentRecord Firds() =>
        new()
        {
            Authority = "ESMA",
            Isin = Isin,
            Mic = "XETA",
            Lei = FundLei,
            Cfi = "CEOGES",
            Currency = "USD",
            FullName = "iShares Core S&P 500 UCITS ETF USD (Acc)",
            RelevantCompetentAuthority = "IE",
            RelevantTradingVenue = "XAMS",
        };

    private static GleifIssuerIdentity Umbrella() =>
        new()
        {
            RequestedIsin = Isin,
            LegalEntityIdentifier = UmbrellaLei,
            LegalName = "ISHARES VII PUBLIC LIMITED COMPANY",
            EntityStatus = "ACTIVE",
            RegistrationStatus = "ISSUED",
            RelatedIsins = [Isin, "IE00B53SZB19", "IE00B3YCGJ38"],
        };

    private static GleifIssuerIdentity Fund() =>
        new()
        {
            RequestedLei = FundLei,
            LegalEntityIdentifier = FundLei,
            LegalName = "iShares Core S&P 500 UCITS ETF",
            EntityStatus = "ACTIVE",
            RegistrationStatus = "ISSUED",
        };

    private static EquityDirectoryListingInput Input(
        GleifIssuerIdentity subFund,
        string umbrella,
        bool product = true
    ) =>
        EquityMarketDirectoryImporter.CreateInput(
            Xetra,
            "xetra-etp",
            Row(),
            Product(),
            Firds(),
            Umbrella(),
            exchangeTradedProduct: product,
            subFund: subFund,
            subFundUmbrella: umbrella
        );

    [Fact]
    public void TheFundUnderTheIsinsUmbrella_IssuesTheListingWithOnlyItsOwnIsin()
    {
        var input = Input(Fund(), UmbrellaLei);

        (input.LegalEntityIdentifier, input.IssuerName, input.SecurityType)
            .Should()
            .Be((FundLei, "iShares Core S&P 500 UCITS ETF", EquitySecurityKind.ExchangeTradedFund));
        input.RelatedIsins.Should().Equal(Isin);
    }

    [Fact]
    public void AProductTheUmbrellaIssuesItself_ClaimsOnlyItsOwnIsin()
    {
        var firds = Firds();
        firds.Lei = UmbrellaLei;

        var input = EquityMarketDirectoryImporter.CreateInput(
            Xetra,
            "xetra-etp",
            Row(),
            Product(),
            firds,
            Umbrella(),
            exchangeTradedProduct: true
        );

        input.LegalEntityIdentifier.Should().Be(UmbrellaLei);
        input.RelatedIsins.Should().Equal(Isin);
    }

    [Fact]
    public void WithoutTheUmbrellaLink_TheSourcesStillDisagree()
    {
        var input = () => Input(null, null);

        input
            .Should()
            .Throw<InvalidDataException>()
            .WithMessage("FIRDS and GLEIF disagree on the security's issuer.");
    }

    [Theory]
    [InlineData("213800AKSTYRLHY3X497", true)]
    [InlineData(UmbrellaLei, false)]
    public void AFundUnderAnotherUmbrellaOrBehindAShare_IsRefused(string umbrella, bool product)
    {
        var input = () => Input(Fund(), umbrella, product);

        input.Should().Throw<InvalidDataException>();
    }
}
