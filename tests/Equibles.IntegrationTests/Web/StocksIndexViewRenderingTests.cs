using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// The Stocks tab partials and Show pages are pinned elsewhere, but the Stocks
/// browser/search list (<c>~/Stocks</c>, <c>Views/Stocks/Index.cshtml</c>) is
/// never rendered through the host — its compiled view stays 0% on the
/// populated path. Pins it: a seeded stock must render in the list with its
/// ticker, name, and a resolved (lowercased) Show link.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksIndexViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public StocksIndexViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetStocks_WithSeededStock_RendersStockRowWithResolvedShowLink()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("AAPL", "the seeded ticker must render in the browser list");
        html.Should().Contain("Apple Inc.", "the seeded company name must render");
        html.Should()
            .Contain(
                "/stocks/aapl",
                "the list row's asp-action=\"Show\" must resolve to the lowercased Show route"
            );
    }
}
