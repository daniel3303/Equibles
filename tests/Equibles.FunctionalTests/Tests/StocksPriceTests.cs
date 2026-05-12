using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksPriceTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksPriceTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Price_GetForSeededTicker_RendersStockHeaderAndPriceTab() {
        // /stocks/{ticker}/price loads the stock, builds a StockDetailViewModel with activeTab=
        // "price", and renders Views/Stocks/Show.cshtml with the Price tab content. Requires a
        // CommonStock row to exist — without one the action returns NotFound. This test seeds
        // one stock, hits the route, and pins the rendered detail header (Ticker - Name). With
        // no DailyStockPrice rows the price chart renders empty but the page itself must still
        // load 200, exercising the LoadStock + LoadPriceTab + TechnicalIndicatorService composition.
        await _web.ResetAndSeedAsync(async db => {
            db.Add(new CommonStock {
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            });
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/price");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // The title comes through ViewData["Title"] into _Layout, but is most visible in the
        // page body via the StockDetailViewModel's Stock.Ticker / Stock.Name rendering.
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("AAPL");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Apple Inc.");
    }
}
