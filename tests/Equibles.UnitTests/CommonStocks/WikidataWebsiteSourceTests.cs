using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.CommonStocks.HostedService.Services;
using Equibles.Integrations.Wikidata.Contracts;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: <c>WikidataWebsiteSource</c> queries the client by the stocks' CIKs
/// and maps the answers back to stock ids; stocks without a CIK are skipped
/// without a query, and an empty batch never hits the client.
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
    public async Task StocksWithoutCik_AreNotQueried()
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
    }
}
