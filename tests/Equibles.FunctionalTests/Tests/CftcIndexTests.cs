using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class CftcIndexTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public CftcIndexTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithEmptyCftcData_RendersFuturesHeaderAndZeroCategoriesDescription() {
        // CftcController.Index queries every CftcContract, groups by Category, and joins each row
        // with the latest CftcPositionReport. The view interpolates Categories.Sum/Count into the
        // description. With no seed rows, the group-by yields zero categories and zero contracts —
        // and the description text must reflect that without nulling out. Catches view regressions
        // where the Sum/Count over an empty IEnumerable wouldn't survive a missing null-guard, or
        // a category enum's NameForHumans is invoked before the empty check.
        await _web.ResetAndSeedAsync();   // guarantee empty DB regardless of test ordering
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/futures");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Futures");
        await Assertions.Expect(page.Locator("p").Filter(new() { HasTextString = "contracts from" }))
            .ToContainTextAsync("0 contracts from");
    }
}
