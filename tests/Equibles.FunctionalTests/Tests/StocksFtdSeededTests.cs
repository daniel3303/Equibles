using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Sec.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksFtdSeededTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksFtdSeededTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Ftd_GetForStockWithLotsOfSeededHistory_RendersMostRecent90DaysAndChart() {
        // Seeds 200 daily FailToDeliver rows so the page has more than
        // StockTabService.LoadFtdTab's Take(90) cap. LoadFtdTab does a two-stage ordering:
        // OrderByDescending(SettlementDate).Take(90) to pick the slice, then OrderBy(Date)
        // for the chart, and the view itself OrderByDescending(SettlementDate) for the table
        // — three different orderings on the same column. The assertion pins four behaviours
        // that the existing empty-state test cannot prove together: (a) the route resolves
        // and returns 200 against a populated table, (b) the Take(90) end-to-end keeps
        // exactly 90 rows on the page (proves the cap and that the chart-section non-empty
        // branch fired), (c) the slice picked the NEWEST 90 days, not the oldest — row 1's
        // SettlementDate must equal the most-recent seeded date, (d) the "Fails to Deliver
        // History" h3 (only rendered in the non-empty branch) is present. AutoDetectChangesEnabled
        // is disabled during the bulk insert so the per-row change-tracker cost stays off
        // the O(n²) path.
        const int totalSeededDays = 200;
        const int displayedRowCap = 90;
        var stockId = Guid.NewGuid();
        var endDate = new DateOnly(2026, 1, 31);

        await _web.ResetAndSeedAsync(async db => {
            db.Add(new CommonStock {
                Id = stockId,
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            });

            db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var i = 0; i < totalSeededDays; i++) {
                db.Add(new FailToDeliver {
                    CommonStockId = stockId,
                    SettlementDate = endDate.AddDays(-i),
                    Quantity = 10_000L + i,
                    Price = 100m + i,
                });
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/ftd");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h3").Filter(new() { HasTextString = "No Fails to Deliver Data" }))
            .ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("h3").Filter(new() { HasTextString = "Fails to Deliver History" }))
            .ToHaveCountAsync(1);

        var rows = page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(displayedRowCap);

        // The view renders OrderByDescending(SettlementDate), so the first row's date column
        // must be the most-recent seeded date — proves the Take(90) selected the newest rows,
        // not the oldest.
        await Assertions.Expect(rows.First.Locator("td").First)
            .ToHaveTextAsync(endDate.ToString("yyyy-MM-dd"));
    }
}
