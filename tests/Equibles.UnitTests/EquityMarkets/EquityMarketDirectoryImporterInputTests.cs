using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.Gleif.Models;

namespace Equibles.UnitTests.EquityMarkets;

public class EquityMarketDirectoryImporterInputTests
{
    private const string ProductUrl =
        "https://live.euronext.com/en/product/equities/FR0000120271-XPAR";
    private const string Lei = "529900S21EQ1BO4ESM68";

    private static EquityMarket Paris => EquityMarketCatalog.TryGet("euronext-paris");

    private static EquityMarketDirectoryRow Row(string currency = "EUR") =>
        new()
        {
            Isin = "FR0000120271",
            MarketIdentifierCode = "XPAR",
            Symbol = "TTE",
            Name = "TOTALENERGIES",
            ReportedCurrency = currency,
            SourceUrl = new Uri(ProductUrl),
        };

    private static EquityMarketDirectoryProduct Product(string currency = null) =>
        new()
        {
            SourceIssuerIdentifier = "002816",
            Name = "TOTALENERGIES",
            SourceUrl = new Uri(ProductUrl),
            ReportedCurrency = currency,
            Evidence = new { issuer_code = "002816" },
        };

    private static FirdsInstrumentRecord Firds(string lei = Lei, string venue = "XPAR") =>
        new()
        {
            Authority = "ESMA",
            Isin = "FR0000120271",
            Mic = "XPAR",
            Lei = lei,
            Cfi = "ESVUFR",
            Currency = "EUR",
            FullName = "TOTALENERGIES SE",
            RelevantCompetentAuthority = "FR",
            RelevantTradingVenue = venue,
        };

    private static GleifIssuerIdentity Issuer(
        string lei = Lei,
        string entityStatus = "ACTIVE",
        string registrationStatus = "ISSUED"
    ) =>
        new()
        {
            RequestedIsin = "FR0000120271",
            LegalEntityIdentifier = lei,
            LegalName = "TotalEnergies SE",
            EntityStatus = entityStatus,
            RegistrationStatus = registrationStatus,
            RelatedIsins = ["FR0000120271", "US89151E1091"],
        };

    [Fact]
    public void AgreeingSources_ProduceAVerifiableListingInputCarryingEveryPieceOfEvidence()
    {
        var input = EquityMarketDirectoryImporter.CreateInput(
            Paris,
            "euronext",
            Row(),
            Product(),
            Firds(),
            Issuer()
        );
        input.Source.Should().Be("euronext");
        input.SourceIssuerIdentifier.Should().Be("002816");
        input.IssuerName.Should().Be("TotalEnergies SE");
        input.LegalEntityIdentifier.Should().Be(Lei);
        input.RelatedIsins.Should().Equal("FR0000120271", "US89151E1091");
        input.Isin.Should().Be("FR0000120271");
        input.Ticker.Should().Be("TTE");
        input.MarketIdentifierCode.Should().Be("XPAR");
        input.MarketCountryCode.Should().Be("FR");
        input.TradingCurrency.Should().Be("EUR");
        input.QuoteUnitMultiplier.Should().Be(1m);
        input.SourceUrl.Should().Be(ProductUrl);
        input.DirectorySnapshotId.Should().BeNull();
        using var payload = JsonDocument.Parse(input.PayloadJson);
        payload.RootElement.GetProperty("Market").GetString().Should().Be("euronext-paris");
        payload.RootElement.GetProperty("Firds").GetProperty("Lei").GetString().Should().Be(Lei);
        payload
            .RootElement.GetProperty("Firds")
            .GetProperty("Cfi")
            .GetString()
            .Should()
            .Be("ESVUFR");
        payload
            .RootElement.GetProperty("Product")
            .GetProperty("issuer_code")
            .GetString()
            .Should()
            .Be("002816");
        payload
            .RootElement.GetProperty("Issuer")
            .GetProperty("LegalName")
            .GetString()
            .Should()
            .Be("TotalEnergies SE");
    }

    [Theory]
    [InlineData("GBp", "GBP", 0.01)]
    [InlineData("GBX", "GBP", 0.01)]
    [InlineData("NOK", "NOK", 1)]
    public void QuotationTokens_AreMappedToTheStoredCurrencyAndMultiplier(
        string token,
        string currency,
        decimal multiplier
    )
    {
        var input = EquityMarketDirectoryImporter.CreateInput(
            Paris,
            "euronext",
            Row(token),
            Product(),
            Firds(),
            Issuer()
        );
        input.TradingCurrency.Should().Be(currency);
        input.QuoteUnitMultiplier.Should().Be(multiplier);
    }

    [Fact]
    public void ProductCurrency_OutranksTheDirectoryRow()
    {
        var input = EquityMarketDirectoryImporter.CreateInput(
            Paris,
            "euronext",
            Row("EUR"),
            Product("GBp"),
            Firds(),
            Issuer()
        );
        input.TradingCurrency.Should().Be("GBP");
        input.QuoteUnitMultiplier.Should().Be(0.01m);
    }

    [Theory]
    [InlineData("ZAc")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownQuotationToken_LeavesTheListingUnverifiedInsteadOfGuessing(string token)
    {
        var input = EquityMarketDirectoryImporter.CreateInput(
            Paris,
            "euronext",
            Row(token),
            Product(),
            Firds(),
            Issuer()
        );
        input.TradingCurrency.Should().BeNull();
        input.QuoteUnitMultiplier.Should().BeNull();
    }

    [Fact]
    public void WithoutAGleifEntity_TheListingCarriesNoLeiAndTheIssuerNameComesFromTheSource()
    {
        var issuer = new GleifIssuerIdentity { RequestedIsin = "FR0000120271" };
        var input = EquityMarketDirectoryImporter.CreateInput(
            Paris,
            "euronext",
            Row(),
            Product(),
            Firds(),
            issuer
        );
        input.LegalEntityIdentifier.Should().BeNull();
        input.RelatedIsins.Should().BeEmpty();
        input.IssuerName.Should().Be("TOTALENERGIES");
    }

    [Fact]
    public void FirdsAndGleifDisagreeingOnTheIssuer_FailsTheRow()
    {
        var create = () =>
            EquityMarketDirectoryImporter.CreateInput(
                Paris,
                "euronext",
                Row(),
                Product(),
                Firds(lei: "969500KLZPNMO8TUM811"),
                Issuer()
            );
        create.Should().Throw<InvalidDataException>().WithMessage("*FIRDS and GLEIF disagree*");
    }

    [Fact]
    public void FirdsWithoutAnIssuerLei_DefersToGleif()
    {
        var input = EquityMarketDirectoryImporter.CreateInput(
            Paris,
            "euronext",
            Row(),
            Product(),
            Firds(lei: null),
            Issuer()
        );
        input.LegalEntityIdentifier.Should().Be(Lei);
    }

    [Theory]
    [InlineData("INACTIVE", "ISSUED")]
    [InlineData("ACTIVE", "RETIRED")]
    [InlineData("ACTIVE", "DUPLICATE")]
    public void ARetiredOrInactiveLegalEntity_FailsTheRow(
        string entityStatus,
        string registrationStatus
    )
    {
        var create = () =>
            EquityMarketDirectoryImporter.CreateInput(
                Paris,
                "euronext",
                Row(),
                Product(),
                Firds(),
                Issuer(entityStatus: entityStatus, registrationStatus: registrationStatus)
            );
        create.Should().Throw<InvalidDataException>().WithMessage("*not a current legal identity*");
    }

    [Fact]
    public void ALapsedRegistration_StillIdentifiesTheIssuer()
    {
        EquityMarketDirectoryImporter
            .CreateInput(
                Paris,
                "euronext",
                Row(),
                Product(),
                Firds(),
                Issuer(registrationStatus: "LAPSED")
            )
            .LegalEntityIdentifier.Should()
            .Be(Lei);
    }

    [Theory]
    [InlineData("product-url")]
    [InlineData("gleif-isin")]
    [InlineData("firds-isin")]
    [InlineData("firds-mic")]
    [InlineData("firds-venue")]
    [InlineData("market-mic")]
    [InlineData("stated-primary-elsewhere")]
    [InlineData("row-url")]
    public void SourcesDisagreeingOnTheSecurity_FailTheRow(string disagreement)
    {
        var row = Row();
        var product = Product();
        var firds = Firds();
        var issuer = Issuer();
        switch (disagreement)
        {
            case "product-url":
                product.SourceUrl = new Uri(
                    "https://live.euronext.com/en/product/equities/FR0000120073-XPAR"
                );
                break;
            case "gleif-isin":
                issuer.RequestedIsin = "FR0000120073";
                break;
            case "firds-isin":
                firds.Isin = "FR0000120073";
                break;
            case "firds-mic":
                firds.Mic = "XAMS";
                break;
            case "firds-venue":
                firds.RelevantTradingVenue = "XAMS";
                break;
            case "market-mic":
                row.MarketIdentifierCode = "XAMS";
                firds.Mic = "XAMS";
                firds.RelevantTradingVenue = "XAMS";
                break;
            case "stated-primary-elsewhere":
                row.StatedPrimaryMarketIdentifierCode = "XAMS";
                break;
            case "row-url":
                row.SourceUrl = null;
                break;
        }
        var create = () =>
            EquityMarketDirectoryImporter.CreateInput(
                Paris,
                "euronext",
                row,
                product,
                firds,
                issuer
            );
        create.Should().Throw<InvalidDataException>().WithMessage("*sources disagree*");
    }
}
