using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksCongressionalTradesSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksCongressionalTradesSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task CongressionalTrades_GetForStockWithSeededTrades_RendersFormattedAmountsAcrossEachCompactBranch()
    {
        // Pins _CongressionalTradesTab.cshtml's non-empty branch + its @functions
        // FormatAmount / FormatCompact helpers, which the empty-state test cannot exercise.
        // FormatCompact has three thresholds (>=1M → "{x}M", >=1K → "{x}K", else "{N0}"):
        // one seeded trade per branch proves all three paths execute end-to-end via the
        // controller → StockTabService → repository → view-rendering pipeline.
        var stockId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var transactionDate = new DateOnly(2026, 3, 15);

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
            db.Add(
                new CongressMember
                {
                    Id = memberId,
                    Name = "Jane Senator",
                    Position = CongressPosition.Senator,
                }
            );
            db.Add(
                new CongressionalTrade
                {
                    CommonStockId = stockId,
                    CongressMemberId = memberId,
                    TransactionDate = transactionDate,
                    FilingDate = transactionDate.AddDays(2),
                    TransactionType = CongressTransactionType.Purchase,
                    OwnerType = "Self",
                    AssetName = "AAPL Common Stock — million-range",
                    AmountFrom = 2_000_000,
                    AmountTo = 5_000_000,
                }
            );
            db.Add(
                new CongressionalTrade
                {
                    CommonStockId = stockId,
                    CongressMemberId = memberId,
                    TransactionDate = transactionDate.AddDays(-1),
                    FilingDate = transactionDate.AddDays(1),
                    TransactionType = CongressTransactionType.Sale,
                    OwnerType = "Spouse",
                    AssetName = "AAPL Common Stock — thousand-range",
                    AmountFrom = 1_000,
                    AmountTo = 15_000,
                }
            );
            db.Add(
                new CongressionalTrade
                {
                    CommonStockId = stockId,
                    CongressMemberId = memberId,
                    TransactionDate = transactionDate.AddDays(-2),
                    FilingDate = transactionDate,
                    TransactionType = CongressTransactionType.Purchase,
                    OwnerType = "Self",
                    AssetName = "AAPL Common Stock — below-thousand",
                    AmountFrom = 100,
                    AmountTo = 999,
                }
            );
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/stocks/aapl/congressionaltrades");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Non-empty branch executed — the empty-state h3 must NOT be present.
        await Assertions
            .Expect(page.Locator("h3").Filter(new() { HasTextString = "No Congressional Trades" }))
            .ToHaveCountAsync(0);

        var rows = page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(3);

        // Row 1 = most-recent (OrderByDescending TransactionDate) — proves the >=1M branch.
        // Asserts FormatAmount("$2M - $5M") + the Purchase→"Buy" badge label + the
        // CongressMember.Position→"Senator" mapping, all three of which live inside the
        // @foreach body's local-var declarations and are only reached on the non-empty branch.
        var amountCells = rows.Locator("td.text-right.font-mono");
        await Assertions.Expect(amountCells.Nth(0)).ToHaveTextAsync("$2M - $5M");
        await Assertions.Expect(amountCells.Nth(1)).ToHaveTextAsync("$1K - $15K");
        await Assertions.Expect(amountCells.Nth(2)).ToHaveTextAsync("$100 - $999");

        // The Sale row renders the "Sell" badge — exercises the ternary branch
        // `badgeClass = Purchase ? "badge-success" : "badge-error"` and the matching label.
        await Assertions.Expect(rows.Nth(1).Locator(".badge")).ToHaveTextAsync("Sell");
        await Assertions.Expect(rows.Nth(0).Locator(".badge")).ToHaveTextAsync("Buy");
    }
}
