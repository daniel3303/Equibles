using System.Text.Json.Nodes;
using Equibles.Integrations.Euronext;
using Equibles.Integrations.Euronext.Models;
using HtmlAgilityPack;

namespace Equibles.UnitTests.Euronext;

public class EuronextInstrumentIdentityTests
{
    private static EuronextEquityListing Listing() =>
        new()
        {
            Isin = "PTALT0AE0002",
            Symbol = "ALTR",
            MarketIdentifierCode = "XLIS",
            SourceUrl = new Uri("https://live.euronext.com/en/product/equities/PTALT0AE0002-XLIS"),
        };

    private static Task<string> Fixture() =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Euronext", "Lisbon", "altri.html")
        );

    [Fact]
    public async Task CapturedProduct_UsesOnlyItsExactInstrumentAndPreservesIssuerEvidence()
    {
        var html = await Fixture();
        html.Should().Contain("NO0010196140");
        var handler = new EuronextDirectoryTestHandler([html]);
        using var http = new HttpClient(handler);
        var identity = await new EuronextDirectoryClient(http).GetInstrumentIdentity(Listing());
        identity.Isin.Should().Be("PTALT0AE0002");
        identity.Symbol.Should().Be("ALTR");
        identity.MarketIdentifierCode.Should().Be("XLIS");
        identity.IssuerCode.Should().Be("115374");
        identity.Name.Should().Be("ALTRI SGPS");
        identity.SourceInstrumentType.Should().Be("STOCK");
        identity.SourceUrl.Should().Be(Listing().SourceUrl);
        JsonNode
            .Parse(identity.RawInstrumentJson)["issuer_code"]
            .GetValue<string>()
            .Should()
            .Be("115374");
        handler.Requests.Should().ContainSingle().Which.Url.Should().Be(Listing().SourceUrl);
    }

    [Theory]
    [InlineData("isin", "NO0010196140")]
    [InlineData("mic", "XOSL")]
    [InlineData("symbol", "NAS")]
    [InlineData("type", "BOND")]
    [InlineData("product_data", "NO0010196140-XOSL")]
    [InlineData("url_type", "bonds")]
    [InlineData("issuer_code", null)]
    [InlineData("issuer_code", " ")]
    [InlineData("name", null)]
    public async Task ConflictingOrMissingIdentity_IsRefused(string field, string value)
    {
        var document = new HtmlDocument();
        document.LoadHtml(await Fixture());
        var script = document.DocumentNode.SelectSingleNode(
            "//script[@data-drupal-selector='drupal-settings-json']"
        );
        var settings = JsonNode.Parse(script.InnerHtml);
        settings["custom"]["instrument"][field] = value;
        script.InnerHtml = settings.ToJsonString();
        var parse = () =>
            EuronextDirectoryParser.ReadInstrumentIdentity(
                Listing(),
                document.DocumentNode.OuterHtml
            );
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("https://example.com/en/product/equities/PTALT0AE0002-XLIS")]
    [InlineData("https://live.euronext.com/en/product/equities/NO0010196140-XOSL")]
    [InlineData("https://live.euronext.com/en/product/equities/PTALT0AE0002-XLIS?other=1")]
    [InlineData("/en/product/equities/PTALT0AE0002-XLIS")]
    [InlineData(null)]
    public async Task InvalidProductUrl_IsRefusedBeforeHttp(string url)
    {
        var listing = Listing();
        listing.SourceUrl = url == null ? null : new Uri(url, UriKind.RelativeOrAbsolute);
        var handler = new EuronextDirectoryTestHandler([]);
        using var http = new HttpClient(handler);
        var fetch = () => new EuronextDirectoryClient(http).GetInstrumentIdentity(listing);
        await fetch.Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().BeEmpty();
    }
}
