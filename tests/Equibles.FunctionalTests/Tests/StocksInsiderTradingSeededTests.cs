using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.InsiderTrading.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksInsiderTradingSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksInsiderTradingSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task InsiderTrading_GetForStockWithLotsOfSeededTransactions_RendersMostRecent100WithIncludedInsiderName()
    {
        // Seeds 200 InsiderTransaction rows across 200 distinct TransactionDates so the page
        // has more than StockTabService.LoadInsiderTradingTab's Take(100) cap. Five
        // InsiderOwners are seeded with distinct CIKs so each row's Include navigation has
        // something to resolve. The assertion pins four behaviours that the existing
        // empty-state test cannot prove together: (a) the route resolves and returns 200
        // against a populated table, (b) the OrderByDescending(TransactionDate).Take(100)
        // end-to-end keeps the most-recent date in row 1, (c) the .Include(t => t.InsiderOwner)
        // executes — without it the insider-name column would render "Unknown" for every
        // row, (d) the non-empty branch of the view runs ("No Insider Trading Data" empty
        // state hidden). AutoDetectChangesEnabled is disabled during the bulk insert so the
        // per-row change-tracker cost stays off the O(n²) path.
        const int totalSeededTransactions = 200;
        const int displayedRowCap = 100;
        const int ownerCount = 5;
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
            var owners = new InsiderOwner[ownerCount];
            for (var o = 0; o < ownerCount; o++)
            {
                owners[o] = new InsiderOwner
                {
                    OwnerCik = $"I{o:D7}",
                    Name = $"Test Insider {o + 1:D2}",
                    IsOfficer = true,
                    OfficerTitle = "Officer",
                };
                db.Add(owners[o]);
            }

            for (var i = 0; i < totalSeededTransactions; i++)
            {
                db.Add(
                    new InsiderTransaction
                    {
                        CommonStockId = stockId,
                        InsiderOwnerId = owners[i % ownerCount].Id,
                        TransactionDate = endDate.AddDays(-i),
                        FilingDate = endDate.AddDays(-i).AddDays(2),
                        TransactionCode = TransactionCode.Purchase,
                        Shares = 1_000 + i,
                        PricePerShare = 100m + i,
                        AcquiredDisposed = AcquiredDisposed.Acquired,
                        SharesOwnedAfter = 10_000 + i,
                        OwnershipNature = OwnershipNature.Direct,
                    }
                );
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/insidertrading");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h3").Filter(new() { HasTextString = "No Insider Trading Data" }))
            .ToHaveCountAsync(0);

        var rows = page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(displayedRowCap);

        // The view renders OrderByDescending(TransactionDate), so the first row's date column
        // must be the most-recent seeded date — proves Take(100) selected the newest rows,
        // not the oldest.
        await Assertions
            .Expect(rows.First.Locator("td").Nth(0))
            .ToHaveTextAsync(endDate.ToString("yyyy-MM-dd"));

        // i=0 (most recent) was assigned owner index 0 → "Test Insider 01". Asserting the
        // insider-name cell carries this value proves the .Include(t => t.InsiderOwner)
        // executed end-to-end — without the Include the view's `t.InsiderOwner?.Name ??
        // "Unknown"` fallback would render "Unknown" for every row.
        await Assertions.Expect(rows.First.Locator("td").Nth(1)).ToHaveTextAsync("Test Insider 01");
    }
}
