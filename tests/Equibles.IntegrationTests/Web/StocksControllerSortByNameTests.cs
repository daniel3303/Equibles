using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to StocksControllerIndexSortAndFilterTests (which pins
/// MarketCapDescending / Ascending and the bad-input fallback). The
/// `StockSort.Name` arm of the switch is unpinned. A refactor that
/// accidentally swapped the OrderBy key to `Ticker` (the Ticker sort is
/// the default fallback, and the arm bodies look superficially similar)
/// would silently flip a UI-visible sort: stocks listed by ticker rather
/// than by company name, which looks correct only for a coincidence —
/// alphabetised tickers don't generally match alphabetised names.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksControllerSortByNameTests
{
    private readonly WebHostFixture _fixture;

    public StocksControllerSortByNameTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Index_SortByName_OrdersAlphabeticallyByCompanyName()
    {
        // Names and tickers are intentionally anti-correlated so a ticker-sort
        // regression cannot accidentally pass: name order is Apex < Mid < Zeta
        // but ticker order is MID < TOP < ZED.
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "ZED",
                    Name = "Apex Large Co.",
                    MarketCapitalization = 1_000d,
                }
            );
            db.Add(
                new CommonStock
                {
                    Ticker = "MID",
                    Name = "Mid Cap Co.",
                    MarketCapitalization = 2_000d,
                }
            );
            db.Add(
                new CommonStock
                {
                    Ticker = "TOP",
                    Name = "Zeta Small Co.",
                    MarketCapitalization = 3_000d,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks?sort=Name");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        var apexIdx = html.IndexOf("Apex Large Co.", StringComparison.Ordinal);
        var midIdx = html.IndexOf("Mid Cap Co.", StringComparison.Ordinal);
        var zetaIdx = html.IndexOf("Zeta Small Co.", StringComparison.Ordinal);
        apexIdx.Should().BeGreaterThan(-1);
        midIdx.Should().BeGreaterThan(apexIdx);
        zetaIdx.Should().BeGreaterThan(midIdx);
    }
}
