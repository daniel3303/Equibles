using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class EconomicDataIndexTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public EconomicDataIndexTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Index_GetWithEmptyFredData_RendersEconomicDataHeaderAndZeroIndicatorsDescription()
    {
        // EconomicDataController.Index queries every FredSeries, groups by Category, and joins each
        // series with its latest observation. The view interpolates Categories.Sum(Series.Count)
        // and Categories.Count into the description. With no seed rows both aggregates must yield
        // 0 — not throw on a nested empty Sum. Catches view regressions where a premature
        // NameForHumans on a missing category, or a missing null-guard on the latest-observation
        // join, would only surface on a clean install with no FRED data yet.
        await _web.ResetAndSeedAsync(); // guarantee empty DB regardless of test ordering
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/economicdata");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Economic Data");
        await Assertions
            .Expect(page.Locator("p").Filter(new() { HasTextString = "indicators from" }))
            .ToContainTextAsync("0 indicators from");
    }
}
