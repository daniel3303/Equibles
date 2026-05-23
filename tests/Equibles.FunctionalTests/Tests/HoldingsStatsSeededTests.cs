using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data.Models;
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
    public async Task Stats_WithTwoQuarters_RendersDistinctAggregatesAndFilerDelta()
    {
        // Contract: /holdings/stats displays per-quarter aggregates where FilerCount,
        // StockCount, and FilingCount use distinct counts — not raw position counts.
        // The summary cards show the latest quarter and the history table shows QoQ
        // filer deltas.
        //
        // Adversarial inputs: Q2 has one filer (A) holding two stocks under the same
        // accession number. A naive COUNT(*) would report 4 filers, 4 stocks, and
        // 4 filings instead of the correct 3, 2, 3 respectively.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var q1 = new DateOnly(2024, 9, 30);
        var q2 = new DateOnly(2024, 12, 31);

        await _web.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                },
                new CommonStock
                {
                    Id = msftId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                }
            );

            var filerA = new InstitutionalHolder { Cik = "F0000001", Name = "Filer A" };
            var filerB = new InstitutionalHolder { Cik = "F0000002", Name = "Filer B" };
            var filerC = new InstitutionalHolder { Cik = "F0000003", Name = "Filer C" };
            db.AddRange(filerA, filerB, filerC);

            // Q1: 2 positions, 2 filers (A,B), 1 stock (AAPL), 2 filings, value=5B
            db.Add(MakeHolding(aaplId, filerA.Id, q1, 100, 2_500_000_000, "ACC-001"));
            db.Add(MakeHolding(aaplId, filerB.Id, q1, 100, 2_500_000_000, "ACC-002"));

            // Q2: 4 positions, 3 distinct filers (A,B,C), 2 distinct stocks, 3 distinct filings, value=10B
            // Filer A holds both stocks under the SAME filing (ACC-003) — tests distinct FilingCount
            db.Add(MakeHolding(aaplId, filerA.Id, q2, 100, 3_000_000_000, "ACC-003"));
            db.Add(MakeHolding(msftId, filerA.Id, q2, 100, 2_000_000_000, "ACC-003"));
            db.Add(MakeHolding(aaplId, filerB.Id, q2, 100, 2_000_000_000, "ACC-004"));
            db.Add(MakeHolding(msftId, filerC.Id, q2, 100, 3_000_000_000, "ACC-005"));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/holdings/stats");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Empty state must NOT be shown
        await Assertions
            .Expect(page.Locator("h2").Filter(new() { HasTextString = "No 13F data yet" }))
            .ToHaveCountAsync(0);

        // --- Summary cards (latest = Q2 2024-12-31) ---
        // The six cards are rendered in a 6-column grid; each has an uppercase label
        // and a bold value below it.
        var cards = page.Locator(".grid .card .card-body");

        // Filers: 3 (distinct A,B,C — not 4 positions)
        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Filers" }).Locator(".font-bold"))
            .ToHaveTextAsync("3");

        // Filings: 3 (distinct ACC-003,ACC-004,ACC-005 — not 4)
        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Filings" }).Locator(".font-bold"))
            .ToHaveTextAsync("3");

        // Stocks: 2 (distinct AAPL,MSFT — not 4)
        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Stocks" }).Locator(".font-bold"))
            .ToHaveTextAsync("2");

        // Positions: 4
        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Positions" }).Locator(".font-bold"))
            .ToHaveTextAsync("4");

        // Avg Pos/Filer: 4/3 = 1.3
        await Assertions
            .Expect(cards.Filter(new() { HasTextString = "Avg Pos/Filer" }).Locator(".font-bold"))
            .ToHaveTextAsync(new Regex(@"1[.,]3"));

        // Total AUM: $10.0B (10,000,000,000 / 1B = 10.0)
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

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        long value,
        string accessionNumber
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Value = value,
            Shares = shares,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accessionNumber,
        };
}
