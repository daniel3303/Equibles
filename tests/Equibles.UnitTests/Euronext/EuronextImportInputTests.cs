using Equibles.CommonStocks.HostedService.Services;
using Equibles.Integrations.Euronext;
using Equibles.Integrations.Euronext.Models;
using Equibles.Integrations.Gleif.Models;

namespace Equibles.UnitTests.Euronext;

public class EuronextImportInputTests
{
    [Theory]
    [InlineData("EUR", "EUR", 1)]
    [InlineData(null, null, null)]
    public void SourceUnitsAndIssuerIdentity_RemainExplicit(
        string currency,
        string expectedCurrency,
        int? expectedScale
    )
    {
        var (listing, product, issuer) = Source();
        listing.ReportedCurrency = currency;
        var input = EuronextEquityDirectoryImporter.CreateInput(listing, product, issuer);
        input.TradingCurrency.Should().Be(expectedCurrency);
        input.QuoteUnitMultiplier.Should().Be(expectedScale);
        input.SourceIssuerIdentifier.Should().Be("115374");
        input.LegalEntityIdentifier.Should().Be(issuer.LegalEntityIdentifier);
        input.RelatedIsins.Should().Equal(issuer.RelatedIsins);
        input.MarketIdentifierCode.Should().Be("XLIS");
        input.MarketCountryCode.Should().Be("PT");
        input.PayloadJson.Should().Contain("PTALT0AE0002").And.Contain("RawInstrumentJson");
    }

    [Theory]
    [InlineData("different-product")]
    [InlineData("different-issuer-isin")]
    [InlineData("different-product-url")]
    [InlineData("different-market")]
    [InlineData("inactive-entity")]
    [InlineData("merged-lei")]
    [InlineData("other-currency")]
    public void InconsistentSourceEvidence_CannotBecomeANativeListing(string scenario)
    {
        var (listing, product, issuer) = Source();
        if (scenario == "different-product")
            product.Isin = "US02209Y1001";
        if (scenario == "different-issuer-isin")
            issuer.RequestedIsin = "US02209Y1001";
        if (scenario == "different-product-url")
            product.SourceUrl = new Uri("https://example.com/product");
        if (scenario == "different-market")
        {
            listing.MarketIdentifierCode = "XPAR";
            product.MarketIdentifierCode = "XPAR";
        }
        if (scenario == "inactive-entity")
            issuer.EntityStatus = "INACTIVE";
        if (scenario == "merged-lei")
            issuer.RegistrationStatus = "MERGED";
        if (scenario == "other-currency")
            listing.ReportedCurrency = "USD";
        var convert = () => EuronextEquityDirectoryImporter.CreateInput(listing, product, issuer);
        convert.Should().Throw<InvalidDataException>();
    }

    private static (EuronextEquityListing, EuronextInstrumentIdentity, GleifIssuerIdentity) Source()
    {
        var url = new Uri("https://live.euronext.com/en/product/equities/PTALT0AE0002-XLIS");
        return (
            new()
            {
                Isin = "PTALT0AE0002",
                Symbol = "ALTR",
                MarketIdentifierCode = "XLIS",
                SourceUrl = url,
                ReportedCurrency = "EUR",
            },
            new()
            {
                Isin = "PTALT0AE0002",
                Symbol = "ALTR",
                MarketIdentifierCode = "XLIS",
                SourceUrl = url,
                IssuerCode = "115374",
                SourceInstrumentType = "STOCK",
                RawInstrumentJson = "{}",
            },
            new()
            {
                RequestedIsin = "PTALT0AE0002",
                LegalEntityIdentifier = "213800AKSTYRLHY3X497",
                LegalName = "ALTRI, S.G.P.S., S.A.",
                EntityStatus = "ACTIVE",
                RegistrationStatus = "ISSUED",
                RelatedIsins = ["PTALT0AE0002", "US02209Y1001"],
            }
        );
    }
}
