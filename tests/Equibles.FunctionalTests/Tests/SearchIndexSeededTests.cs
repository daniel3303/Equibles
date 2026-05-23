using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class SearchIndexSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public SearchIndexSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Search_ForSeededTicker_ReturnsResultsWithQueryPersisted()
    {
        // Seed a stock so the search aggregator has something to find.
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

        // Navigate to the search page — should show the empty state with "Try:" suggestions.
        var response = await page.GotoAsync("/Search");
        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        var searchInput = page.Locator("#global-search-form input[name='q']");
        await Assertions.Expect(searchInput).ToHaveCountAsync(1);

        // The empty state should show category browse chips and "Try:" links.
        await Assertions.Expect(page.Locator("#search-results")).ToContainTextAsync("Try:");

        // Type "AAPL" and submit the search form.
        await searchInput.FillAsync("AAPL");
        await page.Locator("#global-search-form button[type='submit']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The query should persist in the input after submission.
        await Assertions
            .Expect(page.Locator("#global-search-form input[name='q']"))
            .ToHaveValueAsync("AAPL");

        // Results should appear — at least a "Stocks" category with AAPL.
        var resultsContainer = page.Locator("#search-results");
        await Assertions.Expect(resultsContainer).ToContainTextAsync("result");
        await Assertions.Expect(resultsContainer).ToContainTextAsync("AAPL");
        await Assertions.Expect(resultsContainer).ToContainTextAsync("Stocks");
    }
}
