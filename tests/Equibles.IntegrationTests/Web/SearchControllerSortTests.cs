using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Covers the global search <c>sort</c> flow end to end: the SearchController
/// binds <c>?sort=</c> for both the full page (<c>Index</c>) and the instant
/// fragment (<c>Results</c>), and the aggregator applies it. <c>Name</c> must
/// alphabetise hits within a group; <c>Relevance</c> (and an unparseable value,
/// which model-binds to the default) must not 500.
/// </summary>
[Collection(WebHostCollection.Name)]
public class SearchControllerSortTests
{
    private readonly WebHostFixture _fixture;

    public SearchControllerSortTests(WebHostFixture fixture) => _fixture = fixture;

    private async Task SeedTwoStocks()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "GXZ",
                    Name = "Globex Zeta Corp",
                    MarketCapitalization = 1_000_000_000d,
                }
            );
            db.Add(
                new CommonStock
                {
                    Ticker = "GXA",
                    Name = "Globex Alpha Corp",
                    MarketCapitalization = 2_000_000_000d,
                }
            );
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Index_SortByName_AlphabetisesHitsWithinGroup()
    {
        await SeedTwoStocks();

        var response = await _fixture.Client.GetAsync("/Search?q=Globex&sort=Name");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.IndexOf("Globex Alpha Corp", StringComparison.Ordinal)
            .Should()
            .BeLessThan(html.IndexOf("Globex Zeta Corp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Results_SortByRelevance_ReturnsFragmentWithoutError()
    {
        await SeedTwoStocks();

        var response = await _fixture.Client.GetAsync("/Search/Results?q=Globex&sort=Relevance");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Globex");
    }

    [Fact]
    public async Task Index_UnparseableSort_DegradesToDefaultNot500()
    {
        await SeedTwoStocks();

        var response = await _fixture.Client.GetAsync("/Search?q=Globex&sort=not-a-sort");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
