using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsTrendsSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HoldingsTrendsSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Trends_WithTwoQuarters_RendersChartCardsAndCreatesChartInstances()
    {
        // Contract: /holdings/13f-trends reads the per-quarter snapshot rows that
        // HoldingsAggregateRefreshService writes after each 13F import, then
        // renders Chart.js trend charts when the snapshot list is non-empty.
        // The view serializes data inline and Chart.js creates instances on
        // each <canvas> during DOMContentLoaded. Seeding two quarters in
        // reverse insertion order verifies the controller's OrderBy(ReportDate)
        // produces chronological labels for the charts. Aggregate-correctness
        // is covered by HoldingsAggregateRefreshServiceTests at the integration
        // tier; this test pins the rendered chart given already-aggregated rows.
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);

        await _web.ResetAndSeedAsync(async db =>
        {
            // Seed Q2 first (reverse chronological insertion) to verify OrderBy works
            db.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = q2,
                    TotalValue = 5_000_000_000,
                    FilerCount = 2,
                    PositionCount = 2,
                    StockCount = 1,
                    FilingCount = 2,
                }
            );
            db.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = q1,
                    TotalValue = 1_000_000_000,
                    FilerCount = 1,
                    PositionCount = 1,
                    StockCount = 1,
                    FilingCount = 1,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/holdings/13f-trends");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Empty state must NOT be shown
        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "No 13F data yet" }))
            .ToHaveCountAsync(0);

        // --- Chart card headings ---
        await Assertions
            .Expect(
                page.Locator("h2").Filter(new() { HasTextString = "Total Assets Under Management" })
            )
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "Filers and Positions" }))
            .ToHaveCountAsync(1);

        // --- Canvas elements exist (proves the view took the data branch) ---
        await Assertions.Expect(page.Locator("canvas#aum-chart")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("canvas#filer-chart")).ToHaveCountAsync(1);

        // --- Chart.js instances are created (proves the inline script ran) ---
        // Wait for DOMContentLoaded + Chart.js initialization
        await page.WaitForFunctionAsync(
            "() => typeof Chart !== 'undefined' && Chart.getChart(document.getElementById('aum-chart')) != null"
        );

        var aumChartExists = await page.EvaluateAsync<bool>(
            "() => Chart.getChart(document.getElementById('aum-chart')) != null"
        );
        aumChartExists.Should().BeTrue("the AUM chart should be created by Chart.js");

        var filerChartExists = await page.EvaluateAsync<bool>(
            "() => Chart.getChart(document.getElementById('filer-chart')) != null"
        );
        filerChartExists.Should().BeTrue("the filer chart should be created by Chart.js");

        // --- Chart labels are in chronological order (proves OrderBy worked) ---
        var labels = await page.EvaluateAsync<string[]>(
            "() => Chart.getChart(document.getElementById('aum-chart')).data.labels"
        );
        labels.Should().NotBeNull();
        labels.Should().HaveCount(2);
        labels![0].Should().Be("2024-09-30");
        labels[1].Should().Be("2024-12-31");
    }
}
