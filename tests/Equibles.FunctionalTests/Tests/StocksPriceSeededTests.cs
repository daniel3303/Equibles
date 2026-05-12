using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Yahoo.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksPriceSeededTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksPriceSeededTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Price_GetForStockWithLotsOfSeededHistory_RendersIndicatorChartsAndSummaryStats() {
        // Seeds 300 days of daily prices — past SMA200's 200-day window AND past the
        // TakeLast(252) "52W high/low" boundary the view uses — so every indicator in
        // StockTabService.LoadPriceTab + TechnicalIndicatorService (SMA20/50/200, RSI14,
        // MACD line/signal/histogram) has a fully-populated series. Unlike every other Stocks
        // tab, LoadPriceTab has no Take cap and runs the indicator pipeline synchronously
        // over the full result — a regression in any of those computations (or an unhandled
        // exception in the EMA/SMA chain) surfaces as a 500 on this request, not a row-count
        // mismatch. The assertion pins five behaviours that the existing single-row
        // StocksPriceTests cannot prove together: (a) the route returns 200 against 300 rows
        // of seeded prices — i.e., the indicator pipeline survives a non-trivial input
        // length, (b) the non-empty branch of the view runs (the "No Price Data" empty state
        // is hidden), (c) the Price & Moving Averages section renders, (d) the OrderBy(Date)
        // contract holds end-to-end (the Data Range text shows the oldest and newest seeded
        // dates), (e) the latest close summary cell reflects the most-recent seeded close.
        // AutoDetectChangesEnabled is disabled during the bulk insert so the per-row
        // change-tracker cost stays off the O(n²) path.
        const int totalSeededDays = 300;
        var stockId = Guid.NewGuid();
        var endDate = new DateOnly(2026, 1, 30);
        var startDate = endDate.AddDays(-(totalSeededDays - 1));

        await _web.ResetAndSeedAsync(async db => {
            db.Add(new CommonStock {
                Id = stockId,
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            });

            db.ChangeTracker.AutoDetectChangesEnabled = false;
            // i=0 is the OLDEST seeded day, i=totalSeededDays-1 the NEWEST — matches the
            // ascending-by-Date order LoadPriceTab returns and the view's Last()/First()
            // assumptions. Close = 100 + i gives a strictly-increasing series so the
            // newest close (i=299) is deterministic and SMA/RSI/MACD all produce non-NaN
            // values without special-casing.
            for (var i = 0; i < totalSeededDays; i++) {
                var close = 100m + i;
                db.Add(new DailyStockPrice {
                    CommonStockId = stockId,
                    Date = startDate.AddDays(i),
                    Open = close - 0.5m,
                    High = close + 1m,
                    Low = close - 1m,
                    Close = close,
                    AdjustedClose = close,
                    Volume = 1_000_000L + i,
                });
            }
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/price");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h3").Filter(new() { HasTextString = "No Price Data" }))
            .ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("h3").Filter(new() { HasTextString = "Price & Moving Averages" }))
            .ToHaveCountAsync(1);

        // Data Range text is rendered as `{first.Date} — {last.Date}` (em-dash). The text
        // is built from Model.Prices.First()/Last() — LoadPriceTab uses OrderBy(Date), so
        // these have to match the seeded boundaries end-to-end.
        var newestClose = 100m + (totalSeededDays - 1);
        await Assertions.Expect(
                page.Locator("div.font-mono.text-xs").Filter(new() {
                    HasTextString = $"{startDate:yyyy-MM-dd} — {endDate:yyyy-MM-dd}",
                }))
            .ToHaveCountAsync(1);
        await Assertions.Expect(
                page.Locator("div.font-mono.font-semibold").Filter(new() {
                    HasTextString = $"${newestClose:N2}",
                }))
            .ToHaveCountAsync(1);
    }
}
