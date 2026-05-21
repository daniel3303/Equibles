using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksInsiderTradingTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksInsiderTradingTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task InsiderTrading_GetForSeededStockWithNoTransactions_RendersNoInsiderTradingDataEmptyState()
    {
        // /stocks/{ticker}/insidertrading goes through LoadStock + StockTabService.LoadInsiderTradingTab,
        // which queries InsiderTransactionRepository.GetByStock and Includes InsiderOwner. With no
        // transactions seeded, Transactions.Count == 0 and the view takes the empty-state branch.
        // Pins the empty-state h3 copy ("No Insider Trading Data") so a refactor that drops the
        // Count == 0 guard would silently render an empty table instead of the contextual card.
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
        var response = await page.GotoAsync("/stocks/aapl/insidertrading");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "No Insider Trading Data" }))
            .ToHaveCountAsync(1);
    }
}
