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
    public async Task Holdings_GetForStockWithMultipleQuartersAndManyHolders_RendersPositionChangeBuckets()
    {
        // Seeds 4 quarter-end report dates × 200 distinct InstitutionalHolders = 800
        // InstitutionalHolding rows so the page exercises the position-change grouping
        // across substantially more than one ReportDate. Every holder reports the same
        // share count in every quarter, so for the most-recent quarter (vs the one before)
        // all 200 holders land in the Unchanged bucket and the other four buckets are empty.
        // The assertion pins: (a) the date selector lists every distinct ReportDate,
        // (b) the most-recent quarter is selected when no ?date= query string is supplied,
        // (c) all five position-change panels render with the correct counts, (d) the
        // Unchanged bucket's table contains a row per seeded holder (the
        // .Include(h => h.InstitutionalHolder) executes — without it the first column
        // would render "Unknown"). AutoDetectChangesEnabled is disabled during the bulk
        // insert so the per-row change-tracker cost stays off the O(n²) path.
        const int holderCount = 200;
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

        // All five position-change panels are present.
        await Assertions
            .Expect(page.Locator("[data-testid^='holdings-bucket-']"))
            .ToHaveCountAsync(5);

        // Bucket counts: 200 Unchanged, 0 in the other four buckets.
        await Assertions
            .Expect(
                page.Locator("[data-testid='holdings-bucket-unchanged'] .collapse-title .badge")
            )
            .ToHaveTextAsync("200");
        foreach (var emptyBucket in new[] { "new", "increased", "reduced", "soldout" })
        {
            await Assertions
                .Expect(
                    page.Locator(
                        $"[data-testid='holdings-bucket-{emptyBucket}'] .collapse-title .badge"
                    )
                )
                .ToHaveTextAsync("0");
        }

        // Unchanged panel's table renders one row per seeded holder, and the
        // .Include(h => h.InstitutionalHolder) populates the holder name (no "Unknown").
        var unchangedRows = page.Locator(
            "[data-testid='holdings-bucket-unchanged'] table tbody tr"
        );
        await Assertions.Expect(unchangedRows).ToHaveCountAsync(holderCount);
        await Assertions
            .Expect(unchangedRows.First.Locator("td").First)
            .Not.ToHaveTextAsync("Unknown");
    }
}
