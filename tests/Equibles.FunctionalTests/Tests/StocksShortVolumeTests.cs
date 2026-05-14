using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksShortVolumeTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksShortVolumeTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task ShortVolume_GetForStockWithLotsOfSeededHistory_RendersMostRecent90DaysAndChart()
    {
        // Seeds 500 days of daily short-volume rows so the page has substantially more data
        // than StockTabService.LoadShortVolumeTab's Take(90) cap. The assertion pins three
        // behaviours that a smaller fixture cannot prove together: (a) the route resolves and
        // returns 200 against a populated table, (b) the LoadShortVolumeTab Take(90) truncation
        // is honoured end-to-end — fewer rows render than were seeded, (c) the non-empty
        // branch of the view runs (chart canvas present, "No Short Volume Data" empty state
        // hidden). AutoDetectChangesEnabled is disabled during the bulk insert so the per-row
        // change-tracker cost stays off the O(n²) path.
        const int totalSeededDays = 500;
        const int displayedRowCap = 90;
        var stockId = Guid.NewGuid();
        var endDate = new DateOnly(2026, 1, 31);

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

            db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var i = 0; i < totalSeededDays; i++)
            {
                db.Add(
                    new DailyShortVolume
                    {
                        CommonStockId = stockId,
                        Date = endDate.AddDays(-i),
                        ShortVolume = 1_000_000 + i,
                        ShortExemptVolume = 10_000 + i,
                        TotalVolume = 5_000_000 + i,
                        Market = "FNRA",
                    }
                );
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/shortvolume");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h3").Filter(new() { HasTextString = "No Short Volume Data" }))
            .ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#short-volume-chart")).ToHaveCountAsync(1);

        var rows = page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(displayedRowCap);

        // The view renders rows OrderByDescending(Date), so the first row must be the
        // most-recent seeded date — proves Take(90) selected the newest rows, not the oldest.
        await Assertions
            .Expect(rows.First.Locator("td").First)
            .ToHaveTextAsync(endDate.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task ShortVolume_GetForSeededStockWithNoShortVolume_RendersNoShortVolumeDataEmptyState()
    {
        // /stocks/{ticker}/shortvolume runs StockTabService.LoadShortVolumeTab against the
        // seeded stock. With no DailyShortVolume rows, the view takes the explicit empty-state
        // branch ("No Short Volume Data"). Pins both the route + LoadStock lookup AND the
        // empty-state copy — the other test in this file only covers the populated branch.
        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/shortvolume");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h3").Filter(new() { HasTextString = "No Short Volume Data" }))
            .ToHaveCountAsync(1);
    }
}
