using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksShowTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksShowTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Show_GetBareTickerUrl_RedirectsToPriceTab()
    {
        // /stocks/{ticker} is the canonical "default tab" link used across the site (search
        // results, breadcrumbs, McpServer responses). The controller redirects to the Price
        // tab via RedirectToAction(nameof(Price)) — changing the default tab or swapping in a
        // plain View() call would silently land users on a different page and break every
        // cross-link that relies on the bare-ticker URL. The browser follows the 302 so the
        // assertion proves both the redirect target and that the price tab actually renders
        // for a seeded ticker with no price history.
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
        var response = await page.GotoAsync("/stocks/aapl");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);
        page.Url.Should()
            .EndWith("/stocks/aapl/price", "Show must redirect bare-ticker URLs to the Price tab");
    }
}
