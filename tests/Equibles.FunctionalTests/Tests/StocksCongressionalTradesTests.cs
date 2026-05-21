using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksCongressionalTradesTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksCongressionalTradesTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task CongressionalTrades_GetForSeededStockWithNoTrades_RendersNoCongressionalTradesEmptyState()
    {
        // /stocks/{ticker}/congressionaltrades goes through LoadStock +
        // StockTabService.LoadCongressionalTradesTab, which queries
        // CongressionalTradeRepository.GetByStock and Includes CongressMember. With no
        // CongressionalTrade rows the view takes the empty-state branch. Pins the explicit
        // 'No Congressional Trades' h3 — distinct from the Insider/Holdings empty-state copy
        // because it's a different controller action + tab view + repository join.
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
        var response = await page.GotoAsync("/stocks/aapl/congressionaltrades");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "No Congressional Trades" }))
            .ToHaveCountAsync(1);
    }
}
