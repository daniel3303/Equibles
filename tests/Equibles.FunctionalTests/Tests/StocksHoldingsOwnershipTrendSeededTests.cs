using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksHoldingsOwnershipTrendSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksHoldingsOwnershipTrendSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Holdings_GetForStockWithTwoQuarters_RendersOwnershipTrendChart()
    {
        // Two quarters with different share totals so the trend has two points.
        // Pins the golden path end to end through the real host: the chart card,
        // its canvas, and the seeded per-quarter series all render.
        var stockId = Guid.NewGuid();
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
            var holder = new InstitutionalHolder { Cik = "H0000001", Name = "Test Holder" };
            db.Add(holder);
            foreach (
                var (reportDate, shares) in new[]
                {
                    (new DateOnly(2025, 9, 30), 100_000L),
                    (new DateOnly(2025, 12, 31), 150_000L),
                }
            )
            {
                db.Add(
                    new InstitutionalHolding
                    {
                        CommonStockId = stockId,
                        InstitutionalHolder = holder,
                        ReportDate = reportDate,
                        FilingDate = reportDate.AddDays(45),
                        Shares = shares,
                        Value = shares * 10,
                        ShareType = ShareType.Shares,
                        InvestmentDiscretion = InvestmentDiscretion.Sole,
                        AccessionNumber = $"acc-{reportDate:yyyyMMdd}-0001",
                    }
                );
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/holdings");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // The Chart.js bundle (wwwroot/dist) is a build artifact absent from the
        // test harness, so chart *execution* can't be asserted here — the canvas
        // markup and the seeded per-quarter series in the inline script are.
        var canvas = page.Locator("#ownership-trend-chart");
        await Assertions.Expect(canvas).ToHaveCountAsync(1);
        await Assertions.Expect(canvas).ToBeVisibleAsync();

        var content = await page.ContentAsync();
        content.Should().Contain("2025-09-30", "the prior quarter labels the x-axis");
        content.Should().Contain("100000", "the prior quarter's total shares feed the series");
        content.Should().Contain("150000", "the current quarter's total shares feed the series");
    }
}
