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

    [Fact]
    public async Task Show_GetForSeededSeriesWithObservations_RendersStatisticsAndChart()
    {
        // EconomicDataController.Show has a populated-only block `if (values.Length > 0)` that
        // composes DescriptiveStatistics, Median, LatestValue/PreviousValue, and the SMA20/50
        // arrays. The existing test (series only, no observations) skips this entire block, and
        // the view's `if (chronological.Count > 0)` branch (stats cards + #economy-chart) is
        // therefore also untested. Seeds three observations so the populated branch runs and
        // `observations.Count > 1` also sets PreviousValue.
        var seriesId = Guid.NewGuid();
        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new FredSeries
                {
                    Id = seriesId,
                    SeriesId = "DGS10",
                    Title = "Market Yield on U.S. Treasury Securities at 10-Year Constant Maturity",
                    Category = FredSeriesCategory.InterestRates,
                    Frequency = "Daily",
                    Units = "Percent",
                    SeasonalAdjustment = "Not Seasonally Adjusted",
                }
            );
            db.Add(new FredObservation { FredSeriesId = seriesId, Date = new DateOnly(2025, 1, 2), Value = 4.55m });
            db.Add(new FredObservation { FredSeriesId = seriesId, Date = new DateOnly(2025, 1, 3), Value = 4.60m });
            db.Add(new FredObservation { FredSeriesId = seriesId, Date = new DateOnly(2025, 1, 6), Value = 4.58m });
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/economicdata/dgs10");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Stats card heading appears only inside the `chronological.Count > 0` branch.
        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "Statistics" }))
            .ToHaveCountAsync(1);
        // Chart canvas only renders when chronological is non-empty.
        await Assertions.Expect(page.Locator("#economy-chart")).ToHaveCountAsync(1);
    }
}
