using Equibles.CommonStocks.Data.Models;
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
        // Contract: /holdings/trends renders Chart.js trend charts when AumSnapshots
        // is non-empty. The view serializes data inline and Chart.js creates instances
        // on each <canvas> during DOMContentLoaded. Seeding two quarters in reverse
        // insertion order verifies the controller's OrderBy(ReportDate) produces
        // chronological labels for the charts.
        var aaplId = Guid.NewGuid();
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);

        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );

            var filerA = new InstitutionalHolder { Cik = "F0000001", Name = "Filer A" };
            var filerB = new InstitutionalHolder { Cik = "F0000002", Name = "Filer B" };
            db.AddRange(filerA, filerB);

            // Seed Q2 first (reverse chronological insertion) to verify OrderBy works
            db.Add(MakeHolding(aaplId, filerA.Id, q2, 200, 2_000_000_000));
            db.Add(MakeHolding(aaplId, filerB.Id, q2, 300, 3_000_000_000));
            db.Add(MakeHolding(aaplId, filerA.Id, q1, 100, 1_000_000_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/holdings/trends");

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

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Value = value,
            Shares = shares,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };
}
