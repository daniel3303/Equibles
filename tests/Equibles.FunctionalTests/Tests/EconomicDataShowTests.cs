using Equibles.Fred.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class EconomicDataShowTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public EconomicDataShowTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Show_GetForSeededSeriesId_RendersBreadcrumbAndSeriesTitle()
    {
        // /economicdata/{seriesId} looks up the FredSeries case-insensitively (controller
        // uppercases the route value), then queries observations, builds the view model with
        // category display name + frequency expansion + descriptive stats. Seeds one series so
        // the LoadStock-equivalent lookup hits, then asserts the breadcrumb's SeriesId and
        // visible Title. With no FredObservation rows the stats branch is skipped but the page
        // still renders — the breadcrumb is the stable signal.
        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new FredSeries
                {
                    SeriesId = "DGS10",
                    Title = "Market Yield on U.S. Treasury Securities at 10-Year Constant Maturity",
                    Category = FredSeriesCategory.InterestRates,
                    Frequency = "Daily",
                    Units = "Percent",
                    SeasonalAdjustment = "Not Seasonally Adjusted",
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/economicdata/dgs10");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator(".breadcrumbs li").Filter(new() { HasTextString = "DGS10" }))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("body"))
            .ToContainTextAsync("Market Yield on U.S. Treasury Securities");
    }
}
