using Equibles.Cboe.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class MarketVixTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public MarketVixTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Vix_GetWithSeededVixRows_RendersBreadcrumbAndStatsOverEmptyState() {
        // /market/vix loads every CboeVixDaily row, orders descending, and composes a VixViewModel
        // with descriptive statistics (mean/median/min/max/stddev) computed via MathNet. The stats
        // branch only fires when records.Length > 0. Seeding two rows exercises the populated path
        // — the page must render the breadcrumb (Market > VIX) and the table heading. Anchors on
        // the breadcrumb's "VIX" label rather than computed stat text so the test stays stable
        // across MathNet version bumps.
        await _web.ResetAndSeedAsync(async db => {
            db.Add(new CboeVixDaily { Date = new DateOnly(2025, 1, 2), Open = 15m, High = 16m, Low = 14m, Close = 15.5m });
            db.Add(new CboeVixDaily { Date = new DateOnly(2025, 1, 3), Open = 15.5m, High = 17m, Low = 15m, Close = 16.2m });
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/market/vix");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator(".breadcrumbs li").Filter(new() { HasTextString = "VIX" }))
            .ToHaveCountAsync(1);
        // The table renders one row per seeded record (LatestClose number-format depends on
        // thread culture which the fixture doesn't pin, so anchor on the row count instead).
        await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
    }
}
