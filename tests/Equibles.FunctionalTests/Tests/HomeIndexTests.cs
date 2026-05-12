using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HomeIndexTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HomeIndexTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_Get_RendersDataBrowserLandingWithResolvedStocksLink() {
        // Drives the full Kestrel + MVC + Razor pipeline against the real app. Catches
        // regressions in routing, _Layout, the home view, and — crucially — the
        // asp-action/asp-controller tag helpers on the Stocks button. A broken tag helper
        // renders href="" with no static check anywhere, so asserting on the resolved href
        // is the only thing that catches it.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Equibles");

        var stocksLink = page.Locator("a.btn-primary").First;
        var href = await stocksLink.GetAttributeAsync("href");
        // Production sets RouteOptions.LowercaseUrls = true, so the resolved href is lowercase.
        href.Should().StartWith("/stocks",
            "the Stocks button uses asp-action/asp-controller; a broken tag helper renders href=\"\"");
    }
}
