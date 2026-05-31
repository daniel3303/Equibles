using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class SearchInvertedDateRangeTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public SearchInvertedDateRangeTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithInvertedDateRange_RendersResultsWithoutServerError()
    {
        // Contract: SearchAggregator promises "one slow or broken module never breaks the
        // results page" — every provider runs isolated and a thrower/empty group is dropped.
        // dateFrom > dateTo is a well-formed but impossible window a user produces by picking
        // the dates in the wrong order, so the page must still return 2xx (never a 5xx) and the
        // date-unaware Stocks provider must be unaffected by the empty time window.
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

        // Search by company name (not the exact ticker, which would redirect to the stock page)
        // with dateFrom strictly after dateTo — the inverted/empty window under attack.
        var response = await page.GotoAsync(
            "/Search?q=Apple&dateFrom=2025-12-31&dateTo=2000-01-01"
        );

        response.Should().NotBeNull();
        response!
            .Status.Should()
            .BeLessThan(
                500,
                "an inverted date window is client input and must never cause a server error"
            );

        // The page rendered end-to-end and the date-unaware Stocks group survived the impossible
        // window — proves the request completed through the full pipeline, not just a 200 shell.
        var resultsContainer = page.Locator("#search-results");
        await Assertions.Expect(resultsContainer).ToContainTextAsync("AAPL");
        await Assertions.Expect(resultsContainer).ToContainTextAsync("Stocks");
    }
}
