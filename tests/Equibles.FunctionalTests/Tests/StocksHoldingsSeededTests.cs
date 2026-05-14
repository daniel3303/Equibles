using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksHoldingsSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksHoldingsSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Holdings_GetForStockWithMultipleQuartersAndManyHolders_RendersDateSelectorAndTopHundredByValue()
    {
        // Seeds 4 quarter-end report dates × 200 distinct InstitutionalHolders = 800
        // InstitutionalHolding rows so the page has substantially more data than
        // StockTabService.LoadHoldingsTab's Top-100 cap and more than one ReportDate.
        // The assertion pins five behaviours that the existing empty-state test cannot prove
        // together: (a) the date selector lists every distinct ReportDate, (b) the most-recent
        // quarter is selected when no ?date= query string is supplied, (c) the distinct
        // HolderCount aggregation reflects all 200 holders for that quarter, (d) the
        // OrderByDescending(Value).Take(100) end-to-end keeps the highest-Value holder at the
        // top of the rendered table (the InstitutionalHolder.Include is exercised — without it
        // the first column would render "Unknown"), and (e) the "Showing top X of Y" caption
        // appears once DisplayedCount < HolderCount. AutoDetectChangesEnabled is disabled
        // during the bulk insert so the per-row change-tracker cost stays off the O(n²) path.
        const int holderCount = 200;
        const int displayedRowCap = 100;
        var stockId = Guid.NewGuid();
        var reportDates = new[]
        {
            new DateOnly(2025, 3, 31),
            new DateOnly(2025, 6, 30),
            new DateOnly(2025, 9, 30),
            new DateOnly(2025, 12, 31),
        };
        var mostRecentDate = reportDates[^1];

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
            var holders = new InstitutionalHolder[holderCount];
            for (var i = 0; i < holderCount; i++)
            {
                holders[i] = new InstitutionalHolder
                {
                    Cik = $"H{i:D7}",
                    Name = $"Test Holder {i + 1:D3}",
                };
                db.Add(holders[i]);
            }

            foreach (var reportDate in reportDates)
            {
                for (var i = 0; i < holderCount; i++)
                {
                    db.Add(
                        new InstitutionalHolding
                        {
                            CommonStockId = stockId,
                            InstitutionalHolderId = holders[i].Id,
                            ReportDate = reportDate,
                            FilingDate = reportDate.AddDays(45),
                            Value = (long)(i + 1) * 1_000_000L,
                            Shares = (long)(i + 1) * 1_000L,
                            ShareType = ShareType.Shares,
                            InvestmentDiscretion = InvestmentDiscretion.Sole,
                        }
                    );
                }
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/holdings");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions
            .Expect(page.Locator("h3").Filter(new() { HasTextString = "No Holdings Data" }))
            .ToHaveCountAsync(0);

        var dateSelect = page.Locator("select#holdings-date");
        await Assertions.Expect(dateSelect).ToHaveCountAsync(1);
        await Assertions.Expect(dateSelect.Locator("option")).ToHaveCountAsync(reportDates.Length);
        await Assertions.Expect(dateSelect).ToHaveValueAsync(mostRecentDate.ToString("yyyy-MM-dd"));

        var rows = page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(displayedRowCap);

        // The view renders OrderByDescending(Value), so the first row's holder name must be
        // the highest-Value seeded holder (i = holderCount - 1 → "Test Holder 200"). This
        // proves the .Include(h => h.InstitutionalHolder) executes against the populated
        // table — without it the cell would render the "Unknown" fallback.
        await Assertions
            .Expect(rows.First.Locator("td").First)
            .ToHaveTextAsync($"Test Holder {holderCount:D3}");

        // Caption only renders when DisplayedCount < HolderCount — pins both the Top-100 cap
        // and the distinct-holder aggregation across the 200 seeded rows for this quarter.
        await Assertions
            .Expect(
                page.Locator("div.text-xs")
                    .Filter(
                        new()
                        {
                            HasTextString =
                                $"Showing top {displayedRowCap} of {holderCount:N0} holders by value",
                        }
                    )
            )
            .ToHaveCountAsync(1);
    }
}
