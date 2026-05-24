using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsHeatMapSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HoldingsHeatMapSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task HeatMap_WithThreeFilersAcrossTwoQuarters_RendersChartAndTable()
    {
        // Pins the /holdings/heatmap route — requires ≥2 quarters of data and
        // ≥3 filers per stock to surface in the conviction heat map. Verifies
        // heading, bubble chart canvas, score components legend, and the top
        // scorers table with the seeded ticker.
        var stockId = Guid.NewGuid();
        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);

        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );

            var filerA = new InstitutionalHolder { Cik = "0001067983", Name = "Fund Alpha" };
            var filerB = new InstitutionalHolder { Cik = "0001603466", Name = "Fund Beta" };
            var filerC = new InstitutionalHolder { Cik = "0001350694", Name = "Fund Gamma" };
            db.AddRange(filerA, filerB, filerC);

            foreach (var filer in new[] { filerA, filerB, filerC })
            {
                db.Add(MakeHolding(stockId, filer.Id, prior, 100, 1_000_000));
                db.Add(MakeHolding(stockId, filer.Id, latest, 150, 1_500_000));
            }

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/holdings/heatmap");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h1").First)
            .ToContainTextAsync("13F Conviction Heat Map");

        // Bubble chart canvas should render
        await Assertions
            .Expect(page.Locator("canvas[aria-label='13F conviction heat map bubble chart']"))
            .ToHaveCountAsync(1);

        // Score components legend should be visible
        await Assertions.Expect(page.Locator("text=Net Conviction")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("text=Universe Penetration")).ToBeVisibleAsync();

        // Top scorers table should contain the seeded ticker
        var table = page.Locator("table");
        await Assertions.Expect(table.First).ToBeVisibleAsync();
        var tableText = await table.First.Locator("tbody").TextContentAsync();
        tableText.Should().Contain("AAPL");
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
