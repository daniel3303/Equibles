using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Covers the stock browser's sort + minimum-market-cap filters: ordering must
/// follow the requested <c>sort</c>, <c>minMarketCap</c> must exclude smaller
/// companies, and unparseable filter values must degrade to defaults rather
/// than 500 (model binding maps a bad enum or double to the default / null).
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksControllerIndexSortAndFilterTests
{
    private readonly WebHostFixture _fixture;

    public StocksControllerIndexSortAndFilterTests(WebHostFixture fixture) => _fixture = fixture;

    private async Task SeedThreeStocks()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "ZED",
                    Name = "Zeta Small Co.",
                    MarketCapitalization = 100d,
                }
            );
            db.Add(
                new CommonStock
                {
                    Ticker = "MID",
                    Name = "Mid Cap Co.",
                    MarketCapitalization = 5_000_000_000d,
                }
            );
            db.Add(
                new CommonStock
                {
                    Ticker = "TOP",
                    Name = "Apex Large Co.",
                    MarketCapitalization = 9_000_000_000d,
                }
            );
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Index_SortMarketCapDescending_OrdersLargestFirst()
    {
        await SeedThreeStocks();

        var response = await _fixture.Client.GetAsync("/Stocks?sort=MarketCapDescending");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        html.IndexOf("TOP", StringComparison.Ordinal)
            .Should()
            .BeLessThan(html.IndexOf("MID", StringComparison.Ordinal));
        html.IndexOf("MID", StringComparison.Ordinal)
            .Should()
            .BeLessThan(html.IndexOf("ZED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Index_SortMarketCapAscending_OrdersSmallestFirst()
    {
        await SeedThreeStocks();

        var response = await _fixture.Client.GetAsync("/Stocks?sort=MarketCapAscending");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        html.IndexOf("ZED", StringComparison.Ordinal)
            .Should()
            .BeLessThan(html.IndexOf("TOP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Index_MinMarketCap_ExcludesCompaniesBelowThreshold()
    {
        await SeedThreeStocks();

        var response = await _fixture.Client.GetAsync("/Stocks?minMarketCap=1000000000");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("TOP");
        html.Should().Contain("MID");
        html.Should().NotContain(">ZED<");
    }

    [Fact]
    public async Task Index_UnparseableFilterValues_DegradeToDefaultsNot500()
    {
        await SeedThreeStocks();

        var response = await _fixture.Client.GetAsync(
            "/Stocks?sort=not-a-sort&minMarketCap=not-a-number"
        );

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.OK,
                "an invalid sort/minMarketCap must bind to the default / null, "
                    + "not surface a model-binding failure as HTTP 500"
            );
    }
}
