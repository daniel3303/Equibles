using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksFtdTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksFtdTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Ftd_GetForSeededStockWithNoFails_RendersNoFailsToDeliverDataEmptyState()
    {
        // /stocks/{ticker}/ftd goes through LoadStock + StockTabService.LoadFtdTab, which queries
        // FailToDeliverRepository.GetByStock. With no FailToDeliver rows the view takes the
        // empty-state branch. Pins the explicit 'No Fails to Deliver Data' h3 — distinct from
        // the other Stocks-detail tab empty-states because it covers FINRA fails-to-deliver
        // (a separate repository + view than holdings/insider/congressional/documents).
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
        var response = await page.GotoAsync("/stocks/aapl/ftd");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h3").Filter(new() { HasTextString = "No Fails to Deliver Data" }))
            .ToHaveCountAsync(1);
    }
}
