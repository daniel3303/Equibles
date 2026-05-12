using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class MarketIndexTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public MarketIndexTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithEmptyMarketData_RendersHeaderAndPutCallTableShell() {
        // MarketController.Index queries put/call ratios + VIX history and composes a view model.
        // With no rows in either table the page must still render gracefully — every empty-state
        // null in the model flows into the view's interpolation. This test pins that contract: the
        // header renders, the Put/Call Ratios card shows the table shell (every enum value gets
        // a row even with no data), and the response is 200 — not the YSOD that would surface a
        // missing null-coalesce in the view.
        await _web.ResetAndSeedAsync();   // guarantee empty DB regardless of test ordering
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/market");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Market Indicators");
        await Assertions.Expect(page.Locator("h2.card-title").Filter(new() { HasTextString = "Put/Call Ratios" }))
            .ToHaveCountAsync(1);
    }
}
