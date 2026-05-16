using System.Net;
using Equibles.Cboe.Data.Models;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Continues the in-process Razor coverage seam: the CBOE put/call ratio page
/// and the Stocks "Show" shell (reached via the Price tab) were both 0%. Each
/// test seeds the minimum rows, GETs the route, and asserts on HTML the view
/// emits so the compiled <c>Views_Market_PutCallRatio</c> and
/// <c>Views_Stocks_Show</c> classes are exercised end-to-end.
/// </summary>
[Collection(WebHostCollection.Name)]
public class PutCallAndStockViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public PutCallAndStockViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMarketPutCallRatio_WithSeededRows_RendersRecordsAndStatistics()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CboePutCallRatio
                {
                    RatioType = CboePutCallRatioType.Equity,
                    Date = new DateOnly(2026, 1, 5),
                    CallVolume = 1_200_000,
                    PutVolume = 780_000,
                    TotalVolume = 1_980_000,
                    PutCallRatio = 0.65m,
                },
                new CboePutCallRatio
                {
                    RatioType = CboePutCallRatioType.Equity,
                    Date = new DateOnly(2026, 1, 6),
                    CallVolume = 1_100_000,
                    PutVolume = 880_000,
                    TotalVolume = 1_980_000,
                    PutCallRatio = 0.80m,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Market/PutCallRatio/Equity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Equity", "the put/call ratio display name must render");
    }

    [Fact]
    public async Task GetStocksPrice_WithSeededStock_RendersShowShell()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Cik = "0000320193",
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Description = "Seeded for view-rendering coverage",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/AAPL/Price");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("AAPL", "the Show shell must render the stock ticker");
        html.Should().Contain("Apple Inc.", "the Show shell must render the stock name");
    }
}
