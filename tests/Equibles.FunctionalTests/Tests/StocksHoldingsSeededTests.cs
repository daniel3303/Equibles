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
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "No Holdings Data" }))
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

        // No movers in this fixture (every holder reports the same shares quarter over
        // quarter), so the Top Buyers / Top Sellers cards must NOT render.
        await Assertions
            .Expect(page.Locator("[data-testid='holdings-top-buyers']"))
            .ToHaveCountAsync(0);
        await Assertions
            .Expect(page.Locator("[data-testid='holdings-top-sellers']"))
            .ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Holdings_GetForStockWithQuarterlyMovement_RendersTopBuyersAndTopSellersCards()
    {
        // Seeds two quarters with explicit movement so the Top Buyers / Top Sellers
        // cards have something to rank:
        //   Q1 (prior): holders 1..4 hold 1_000 shares each. Holder 5 does not exist.
        //   Q2 (latest): holder 1 → 1_500 (Increased Δ +500),
        //                holder 2 → 800   (Reduced Δ -200),
        //                holder 3 → gone  (Sold out Δ -1_000),
        //                holder 4 → 1_000 (Unchanged),
        //                holder 5 → 2_000 (New Δ +2_000).
        // Expected Top Buyers: Holder 5 (+2_000), Holder 1 (+500).
        // Expected Top Sellers: Holder 3 (-1_000), Holder 2 (-200).
        var stockId = Guid.NewGuid();
        var anchorStockId = Guid.NewGuid();
        var prior = new DateOnly(2025, 6, 30);
        var latest = new DateOnly(2025, 9, 30);

        await _web.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                }
            );
            // Anchor stock — gives Holder 3 a Q2 13F filing on a different
            // ticker so the Sold-Out filter recognises them as "filed this
            // quarter, just not for MSFT" (genuine signal) instead of
            // "hasn't filed yet" (which is now correctly excluded).
            db.Add(
                new CommonStock
                {
                    Id = anchorStockId,
                    Ticker = "ANCH",
                    Name = "Anchor Corp.",
                    Cik = "0000999999",
                }
            );

            var holders = new InstitutionalHolder[5];
            for (var i = 0; i < holders.Length; i++)
            {
                holders[i] = new InstitutionalHolder
                {
                    Cik = $"M{i + 1:D7}",
                    Name = $"Mover Holder {i + 1}",
                };
                db.Add(holders[i]);
            }

            // Holder 3 reports a Q2 13F against the anchor stock — proves to
            // the Sold-Out filter that they ARE filing the latest quarter,
            // they just exited MSFT.
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = anchorStockId,
                    InstitutionalHolderId = holders[2].Id,
                    ReportDate = latest,
                    FilingDate = latest.AddDays(45),
                    Value = 1_000_000,
                    Shares = 1_000,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                }
            );

            // Q1 (prior) — holders 1..4 only
            for (var i = 0; i < 4; i++)
            {
                db.Add(
                    new InstitutionalHolding
                    {
                        CommonStockId = stockId,
                        InstitutionalHolderId = holders[i].Id,
                        ReportDate = prior,
                        FilingDate = prior.AddDays(45),
                        Value = 1_000_000,
                        Shares = 1_000,
                        ShareType = ShareType.Shares,
                        InvestmentDiscretion = InvestmentDiscretion.Sole,
                    }
                );
            }

            // Q2 (latest)
            var latestShares = new long[]
            {
                1_500,
                800, /* holder 3 sold out */
                0,
                1_000,
                2_000,
            };
            for (var i = 0; i < holders.Length; i++)
            {
                if (latestShares[i] == 0)
                    continue;
                db.Add(
                    new InstitutionalHolding
                    {
                        CommonStockId = stockId,
                        InstitutionalHolderId = holders[i].Id,
                        ReportDate = latest,
                        FilingDate = latest.AddDays(45),
                        Value = latestShares[i] * 1_000,
                        Shares = latestShares[i],
                        ShareType = ShareType.Shares,
                        InvestmentDiscretion = InvestmentDiscretion.Sole,
                    }
                );
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/msft/holdings");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Cards present.
        await Assertions
            .Expect(page.Locator("[data-testid='holdings-top-buyers']"))
            .ToHaveCountAsync(1);
        await Assertions
            .Expect(page.Locator("[data-testid='holdings-top-sellers']"))
            .ToHaveCountAsync(1);

        // Buyer count badge (2 = New + Increased) and seller count badge (2 = Reduced + Sold out).
        await Assertions
            .Expect(page.Locator("[data-testid='holdings-top-buyers'] .badge").First)
            .ToHaveTextAsync("2");
        await Assertions
            .Expect(page.Locator("[data-testid='holdings-top-sellers'] .badge").First)
            .ToHaveTextAsync("2");

        // Top buyer row is Holder 5 (+2_000); top seller row is Holder 3 (-1_000).
        await Assertions
            .Expect(
                page.Locator("[data-testid='holdings-top-buyers'] table tbody tr")
                    .First.Locator("td")
                    .First
            )
            .ToHaveTextAsync("Mover Holder 5");
        await Assertions
            .Expect(
                page.Locator("[data-testid='holdings-top-sellers'] table tbody tr")
                    .First.Locator("td")
                    .First
            )
            .ToHaveTextAsync("Mover Holder 3");
    }
}
