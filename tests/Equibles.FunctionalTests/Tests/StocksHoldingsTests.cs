using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksHoldingsTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksHoldingsTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Holdings_GetForSeededStockWithNoHoldings_RendersNoHoldingsDataEmptyState() {
        // /stocks/{ticker}/holdings runs StockTabService.LoadHoldingsTab against the seeded
        // stock. With no InstitutionalHolding rows, AvailableDates is empty and the view takes
        // the explicit empty-state branch ("No Holdings Data"). This pins both the route +
        // LoadStock lookup AND the empty-state copy — distinct from StocksPriceTests, which
        // does not have an empty-state branch (a no-prices chart still renders).
        await _web.ResetAndSeedAsync(async db => {
            db.Add(new CommonStock {
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            });
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/holdings");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h3").Filter(new() { HasTextString = "No Holdings Data" }))
            .ToHaveCountAsync(1);
    }
}
