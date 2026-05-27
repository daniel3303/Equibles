using System.Text.RegularExpressions;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HoldingsStatsSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HoldingsStatsSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Stats_WithTwoQuartersOfSnapshots_RendersAggregatesAndFilerDelta()
    {
        // Contract: /holdings/13f-statistics reads the per-quarter snapshot rows that
        // HoldingsAggregateRefreshService writes after each 13F import. The
        // summary cards show the latest quarter and the history table shows
        // QoQ filer deltas. Aggregate-correctness (distinct filer / stock /
        // filing counts) is covered by HoldingsAggregateRefreshServiceTests
        // at the integration tier; this test pins the rendered numbers given
        // already-aggregated snapshot rows.
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);

        await _web.ResetAndSeedAsync(async db =>
        {
            // Same shape the live aggregate would have produced for the
            // legacy test's holdings: Q1 has 2 filers / 1 stock / 2 filings,
            // Q2 has 3 filers / 2 stocks / 3 filings (Filer A holds both
            // stocks under the same accession).
            db.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = q1,
                    TotalValue = 5_000_000_000,
                    FilerCount = 2,
                    PositionCount = 2,
                    StockCount = 1,
                    FilingCount = 2,
                }
            );
            db.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = q2,
                    TotalValue = 10_000_000_000,
                    FilerCount = 3,
                    PositionCount = 4,
                    StockCount = 2,
                    FilingCount = 3,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/holdings/13f-statistics");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Empty state must NOT be shown
        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "No 13F data yet" }))
            .ToHaveCountAsync(0);

        // --- Summary cards (latest = Q2 2024-12-31) ---
        var cards = page.Locator(".grid .card .card-body");

        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Filers" }).Locator(".font-bold"))
            .ToHaveTextAsync("3");

        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Filings" }).Locator(".font-bold"))
            .ToHaveTextAsync("3");

        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Stocks" }).Locator(".font-bold"))
            .ToHaveTextAsync("2");

        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Positions" }).Locator(".font-bold"))
            .ToHaveTextAsync("4");

        // Avg Pos/Filer: 4/3 = 1.3
        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Avg Pos/Filer" }).Locator(".font-bold"))
            .ToHaveTextAsync(new Regex(@"1[.,]3"));

        // Total AUM: $10.0B
        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Total AUM" }).Locator(".font-bold"))
            .ToHaveTextAsync(new Regex(@"\$10[.,]0B"));

        // --- Quarterly History Table ---
        var table = page.Locator("[data-testid='stats-table']");
        await Assertions.Expect(table).ToHaveCountAsync(1);

        var rows = table.Locator("tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(2);

        // Row 0 is the latest quarter (2024-12-31). Filer delta = 3 - 2 = +1.
        await Assertions.Expect(rows.Nth(0).Locator("td").Nth(0)).ToHaveTextAsync("2024-12-31");
        await Assertions.Expect(rows.Nth(0).Locator("td").Nth(1)).ToContainTextAsync("+1");

        // Row 1 is Q1 (2024-09-30). No prior quarter so no delta rendered.
        await Assertions.Expect(rows.Nth(1).Locator("td").Nth(0)).ToHaveTextAsync("2024-09-30");
    }
}
