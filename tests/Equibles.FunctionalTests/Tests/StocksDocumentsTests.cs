using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksDocumentsTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksDocumentsTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Documents_GetForSeededStockWithNoDocuments_RendersNoDocumentsAvailableEmptyState()
    {
        // /stocks/{ticker}/documents goes through LoadStock + StockTabService.LoadDocumentsTab,
        // which queries DocumentRepository.GetByCompany. With no Document rows the view takes
        // the empty-state branch. Pins the explicit 'No Documents Available' h3 — distinct from
        // the Insider/Holdings/Congressional empty-states because it covers SEC filings + earnings
        // transcripts, a different repository join (DocumentRepository, not InsiderTransaction or
        // CongressionalTrade).
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
        var response = await page.GotoAsync("/stocks/aapl/documents");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "No Documents Available" }))
            .ToHaveCountAsync(1);
    }
}
