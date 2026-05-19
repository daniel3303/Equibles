using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Covers the global search date-range binding: SearchController binds
/// <c>?dateFrom=</c>/<c>?dateTo=</c> (DateOnly) for both the full page
/// (<c>Index</c>) and the instant fragment (<c>Results</c>) and threads them
/// through the aggregator. Date-unaware providers (e.g. Stocks) ignore the
/// bounds, so a stock query still returns with a range applied; an
/// unparseable date model-binds to null rather than 500.
/// </summary>
[Collection(WebHostCollection.Name)]
public class SearchControllerDateFilterTests
{
    private readonly WebHostFixture _fixture;

    public SearchControllerDateFilterTests(WebHostFixture fixture) => _fixture = fixture;

    private async Task SeedStock()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "GXA",
                    Name = "Globex Alpha Corp",
                    MarketCapitalization = 1_000_000_000d,
                }
            );
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Index_WithDateRange_BindsAndStillReturnsDateUnawareGroups()
    {
        await SeedStock();

        var response = await _fixture.Client.GetAsync(
            "/Search?q=Globex&dateFrom=2024-01-01&dateTo=2024-12-31"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Globex Alpha Corp");
    }

    [Fact]
    public async Task Results_WithDateFromOnly_ReturnsFragmentWithoutError()
    {
        await SeedStock();

        var response = await _fixture.Client.GetAsync(
            "/Search/Results?q=Globex&dateFrom=2023-06-15"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Index_UnparseableDate_DegradesToNullNot500()
    {
        await SeedStock();

        var response = await _fixture.Client.GetAsync("/Search?q=Globex&dateFrom=not-a-date");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
