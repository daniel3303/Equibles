using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksShortInterestTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksShortInterestTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task ShortInterest_GetForStockWithLotsOfSeededHistory_RendersMostRecent24SnapshotsAndChart() {
        // Seeds 100 biweekly short-interest snapshots (~4 years) so the page has substantially
        // more history than StockTabService.LoadShortInterestTab's Take(24) cap. The assertion
        // pins three behaviours together: (a) the route resolves and returns 200 against a
        // populated table, (b) the LoadShortInterestTab Take(24) truncation is honoured end-to-end
        // — fewer rows render than were seeded, (c) the non-empty branch of the view runs (chart
        // canvas present, "No Short Interest Data" empty state hidden). AutoDetectChangesEnabled
        // is disabled during the bulk insert so the per-row change-tracker cost stays off the
        // O(n²) path.
        const int totalSeededSnapshots = 100;
        const int displayedRowCap = 24;
        var stockId = Guid.NewGuid();
        var newestSettlement = new DateOnly(2026, 1, 15);

        await _web.ResetAndSeedAsync(async db => {
            db.Add(new CommonStock {
                Id = stockId,
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            });

            db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var i = 0; i < totalSeededSnapshots; i++) {
                db.Add(new ShortInterest {
                    CommonStockId = stockId,
                    SettlementDate = newestSettlement.AddDays(-14 * i),
                    CurrentShortPosition = 50_000_000 + i,
                    PreviousShortPosition = 49_000_000 + i,
                    ChangeInShortPosition = 1_000_000,
                    AverageDailyVolume = 80_000_000,
                    DaysToCover = 0.63m,
                });
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/shortinterest");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h3").Filter(new() { HasTextString = "No Short Interest Data" }))
            .ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#short-interest-chart")).ToHaveCountAsync(1);

        var rows = page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(displayedRowCap);

        // View renders OrderByDescending(SettlementDate), so the first row must match the newest
        // seeded date — proves Take(24) selected the newest snapshots, not the oldest.
        await Assertions.Expect(rows.First.Locator("td").First)
            .ToHaveTextAsync(newestSettlement.ToString("yyyy-MM-dd"));
    }
}
