using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksIndexTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksIndexTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithEmptyDatabase_RendersZeroCountAndSearchForm() {
        // The /stocks page runs the EF Search query against the real ParadeDB container, paginates,
        // and renders the result list. The functional fixture seeds no rows, so the test pins the
        // empty-state shape: 200 status, "Stocks" h1, total count of 0, and a search input named
        // "search" so the query-string parameter round-trips through asp-for. A renamed parameter
        // on the controller or a broken tag helper would change the rendered name attribute and
        // silently break URL-based searching.
        await _web.ResetAndSeedAsync();   // guarantee empty DB regardless of test ordering
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/stocks");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Stocks");
        await Assertions.Expect(page.Locator("p").Filter(new() { HasTextString = "Browse" }))
            .ToContainTextAsync("0");

        var searchInput = page.Locator("input[name='search']");
        await Assertions.Expect(searchInput).ToHaveCountAsync(1);
    }
}
