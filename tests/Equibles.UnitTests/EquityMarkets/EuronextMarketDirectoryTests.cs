using Equibles.Integrations.Euronext;
using Equibles.Integrations.Euronext.Models;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.EquityMarkets;

public class EuronextMarketDirectoryTests
{
    private static Task<string> Fixture(string market, string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Euronext", market, name)
        );

    [Fact]
    public async Task ParisDirectory_ExposesItsOwnGatewayCoveringEveryParisVenue()
    {
        var html = await Fixture("Paris", "directory.html");
        var gateway = EuronextDirectoryParser.ReadGateway(html, EuronextMarket.Paris);
        gateway.AbsolutePath.Should().Be("/en/product_directory/data/stocks-paris");
        gateway.Query.Should().Contain("mics=");
        var lisbon = () => EuronextDirectoryParser.ReadGateway(html, EuronextMarket.Lisbon);
        lisbon.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task ParisProduct_ConfirmsTheDirectoryRowAndStatesItsIssuerCode()
    {
        var listing = new EuronextEquityListing
        {
            Isin = "FR0000120271",
            Symbol = "TTE",
            MarketIdentifierCode = "XPAR",
            SourceUrl = new Uri("https://live.euronext.com/en/product/equities/FR0000120271-XPAR"),
        };
        var handler = new EuronextDirectoryTestHandler([
            await Fixture("Paris", "totalenergies.html"),
        ]);
        using var http = new HttpClient(handler);
        var identity = await new EuronextDirectoryClient(http).GetInstrumentIdentity(listing);
        identity.Name.Should().Be("TOTALENERGIES");
        identity.IssuerCode.Should().Be("002816");
        identity.SourceInstrumentType.Should().Be("STOCK");
        identity.MarketIdentifierCode.Should().Be("XPAR");
    }

    [Fact]
    public void EveryMarket_HasAUniqueSlugDisjointVenuesAndALocationCode()
    {
        EuronextMarket.All.Select(market => market.Slug).Should().OnlyHaveUniqueItems();
        EuronextMarket
            .All.SelectMany(market => market.MarketIdentifierCodes)
            .Should()
            .OnlyHaveUniqueItems();
        EuronextMarket
            .All.Select(market => market.TradesLocationCode)
            .Should()
            .OnlyHaveUniqueItems();
        EuronextMarket.FromSlug("lisbon").Should().BeSameAs(EuronextMarket.Lisbon);
        EuronextMarket.FromSlug("frankfurt").Should().BeNull();
        EuronextMarket.ByMarketIdentifierCode("XPAR").Should().BeSameAs(EuronextMarket.Paris);
        EuronextMarket.ByMarketIdentifierCode("XETR").Should().BeNull();
        EuronextMarket
            .Milan.DirectoryUrl.AbsoluteUri.Should()
            .Be("https://live.euronext.com/en/markets/milan/equities/list");
    }

    [Fact]
    public async Task LisbonPage_IsRefusedUnderAnotherMarketsVenueSet()
    {
        var body = await Fixture("Lisbon", "equities.json");
        EuronextDirectoryParser
            .ReadPage(body, EuronextMarket.Lisbon)
            .Listings.Should()
            .HaveCount(49);
        var paris = () => EuronextDirectoryParser.ReadPage(body, EuronextMarket.Paris);
        paris.Should().Throw<InvalidDataException>();
    }
}
