using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class CftcShowTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public CftcShowTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Show_GetWithUnknownMarketCode_Returns404() {
        // CftcController.Show has two not-found branches: empty/whitespace marketCode, and a
        // valid code that doesn't match any contract. With no seed rows, every code falls into
        // the second branch. This test pins the wire-level NotFound contract — a regression
        // that returns a 200 with an empty view (e.g., a controller refactor that drops the
        // null-check on the contract lookup) would only surface here.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/futures/NOT_A_REAL_MARKET_CODE");

        response.Should().NotBeNull();
        response!.Status.Should().Be(404);
    }
}
