using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.CommonStocks.HostedService.Services;
using Equibles.Integrations.Wikidata.Contracts;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: <c>WikidataWebsiteSource</c> queries the client by the stocks' CIKs,
/// and by LEI for the CIK-less issuers that carry one, and maps the answers back
/// to stock ids; stocks with neither key are skipped without a query, and an
/// empty batch never hits the client.
/// </summary>
public class WikidataWebsiteSourceTests
{
    [Fact]
    public async Task AnswersAreKeyedBackToStockIds()
    {
        var withAnswer = new WebsiteSourceStock(Guid.NewGuid(), "AAPL", "320193");
        var withoutAnswer = new WebsiteSourceStock(Guid.NewGuid(), "ZZZZ", "999999");
        var client = Substitute.For<IWikidataClient>();
        client
            .GetOfficialWebsitesByCik(
                Arg.Is<IReadOnlyCollection<string>>(c => c.Contains("320193")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<string, string> { ["320193"] = "https://apple.com/" });

        var result = await new WikidataWebsiteSource(client).FindWebsites(
            [withAnswer, withoutAnswer],
            CancellationToken.None
        );

        result
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<Guid, string>(withAnswer.Id, "https://apple.com/"));
    }

    [Fact]
    public async Task TwoStocksSharingACik_BothReceiveTheWebsite()
    {
        // Dual-class issuers (e.g. GOOGL/GOOG) are separate stocks that share one CIK; Wikidata
        // keys websites by CIK, so both tickers should receive the resolved website — not just one.
        var classA = new WebsiteSourceStock(Guid.NewGuid(), "GOOGL", "1652044");
        var classC = new WebsiteSourceStock(Guid.NewGuid(), "GOOG", "1652044");
        var client = Substitute.For<IWikidataClient>();
        client
            .GetOfficialWebsitesByCik(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<string, string> { ["1652044"] = "https://abc.xyz/" });

        var result = await new WikidataWebsiteSource(client).FindWebsites(
            [classA, classC],
            CancellationToken.None
        );

        result.Should().ContainKeys(classA.Id, classC.Id);
    }

    [Fact]
    public async Task StocksWithoutCikOrLei_AreNotQueried()
    {
        var noCik = new WebsiteSourceStock(Guid.NewGuid(), "AAA", null);
        var blankCik = new WebsiteSourceStock(Guid.NewGuid(), "BBB", " ");
        var client = Substitute.For<IWikidataClient>();

        var result = await new WikidataWebsiteSource(client).FindWebsites(
            [noCik, blankCik],
            CancellationToken.None
        );

        result.Should().BeEmpty();
        await client
            .DidNotReceive()
            .GetOfficialWebsitesByCik(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
        await client
            .DidNotReceive()
            .GetOfficialWebsitesByLei(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CiklessIssuerWithLei_IsQueriedByLei()
    {
        var paris = new WebsiteSourceStock(
            Guid.NewGuid(),
            "BNP",
            null,
            LegalEntityIdentifier: "R0MUWSFPU8MPRO8K5P83",
            Isin: "FR0000131104",
            MarketIdentifierCode: "XPAR",
            MarketCountryCode: "FR"
        );
        var client = Substitute.For<IWikidataClient>();
        client
            .GetOfficialWebsitesByLei(
                Arg.Is<IReadOnlyCollection<string>>(c => c.Contains("R0MUWSFPU8MPRO8K5P83")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new Dictionary<string, string>
                {
                    ["R0MUWSFPU8MPRO8K5P83"] = "https://group.bnpparibas",
                }
            );

        var result = await new WikidataWebsiteSource(client).FindWebsites(
            [paris],
            CancellationToken.None
        );

        result
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<Guid, string>(paris.Id, "https://group.bnpparibas"));
        await client
            .DidNotReceive()
            .GetOfficialWebsitesByCik(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MixedBatch_QueriesEachKeyKindOnce_AndAnSecRegistrantNeverJoinsByLei()
    {
        // A registrant with both keys goes by CIK only, so a stale Wikidata LEI can never
        // override the SEC-keyed answer; the CIK-less issuer goes by LEI.
        var registrant = new WebsiteSourceStock(
            Guid.NewGuid(),
            "AAPL",
            "320193",
            LegalEntityIdentifier: "HWUPKR0MPOU8FGXBT394"
        );
        var venueOnly = new WebsiteSourceStock(
            Guid.NewGuid(),
            "BNP",
            null,
            LegalEntityIdentifier: "R0MUWSFPU8MPRO8K5P83",
            MarketIdentifierCode: "XPAR",
            MarketCountryCode: "FR"
        );
        var client = Substitute.For<IWikidataClient>();
        client
            .GetOfficialWebsitesByCik(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<string, string> { ["320193"] = "https://apple.com/" });
        client
            .GetOfficialWebsitesByLei(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new Dictionary<string, string>
                {
                    ["R0MUWSFPU8MPRO8K5P83"] = "https://group.bnpparibas",
                }
            );

        var result = await new WikidataWebsiteSource(client).FindWebsites(
            [registrant, venueOnly],
            CancellationToken.None
        );

        result.Should().HaveCount(2);
        result[registrant.Id].Should().Be("https://apple.com/");
        result[venueOnly.Id].Should().Be("https://group.bnpparibas");
        await client
            .Received(1)
            .GetOfficialWebsitesByLei(
                Arg.Is<IReadOnlyCollection<string>>(c =>
                    c.Count == 1 && c.Contains("R0MUWSFPU8MPRO8K5P83")
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
