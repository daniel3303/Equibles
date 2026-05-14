using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksShowHolderTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksShowHolderTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task ShowHolder_GetForSeededTickerAndCik_RendersHolderNameAndBreadcrumb()
    {
        // /stocks/{ticker}/holders/{cik} requires both the stock (by uppercased ticker) AND the
        // institutional holder (by CIK) to exist — either missing returns 404. With both seeded
        // and no InstitutionalHolding rows the holder detail view renders the holder name as h1
        // and a Stocks > {Ticker} > {HolderName} breadcrumb. This pins both lookups + the
        // composition path with an empty holdings history.
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
            db.Add(new InstitutionalHolder { Cik = "0001067983", Name = "Berkshire Hathaway Inc" });
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/holders/0001067983");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Berkshire Hathaway Inc");
        await Assertions
            .Expect(page.Locator(".breadcrumbs li").Filter(new() { HasTextString = "Berkshire" }))
            .ToHaveCountAsync(1);
    }
}
