using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksIndexSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksIndexSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithSeededStock_RendersTickerAndCountOne()
    {
        // Smoke test for WebAppFixture.ResetAndSeedAsync — proves the seed delegate's writes
        // are visible to the running app's DbContext (same connection, same schema) and that
        // Respawn doesn't accidentally truncate them between the seed call and the HTTP request.
        // Stocks/Index runs CommonStockRepository.Search, which queries the live DB; a single
        // seeded row must appear in the rendered list.
        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("p").Filter(new() { HasTextString = "Browse" }))
            .ToContainTextAsync("1 US common stocks");
        await Assertions.Expect(page.Locator("text=AAPL")).ToBeVisibleAsync();
    }
}
