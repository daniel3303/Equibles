using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.InsiderTrading.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InsiderDashboardSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InsiderDashboardSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Dashboard_WithMixedBuysAndSells_SegregatesByTypeAndOrdersByDollarValue()
    {
        // Contract: /insider-trading/dashboard renders three ranked tables from the
        // last 90 days — TopBuys (purchases only), TopSells (sales only), and
        // BiggestTransactions (all types). Each is ordered by Shares * PricePerShare
        // descending. Insider names and stock tickers must resolve (proves the LINQ
        // projection navigates InsiderOwner and CommonStock).
        //
        // Adversarial inputs: dollar values are chosen so naive ordering by shares
        // alone or price alone would fail — the highest-value purchase has fewer
        // shares than another but a higher price.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stockAaplId = Guid.NewGuid();
        var stockMsftId = Guid.NewGuid();

        await _web.ResetAndSeedAsync(async db =>
        {
            var aapl = new CommonStock
            {
                Id = stockAaplId,
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            };
            var msft = new CommonStock
            {
                Id = stockMsftId,
                Ticker = "MSFT",
                Name = "Microsoft Corporation",
                Cik = "0000789019",
            };
            db.Add(aapl);
            db.Add(msft);

            var buyer = new InsiderOwner
            {
                OwnerCik = "B0000001",
                Name = "Alice Buyer",
                IsOfficer = true,
                OfficerTitle = "CEO",
            };
            var seller = new InsiderOwner
            {
                OwnerCik = "S0000001",
                Name = "Bob Seller",
                IsOfficer = true,
                OfficerTitle = "CFO",
            };
            db.Add(buyer);
            db.Add(seller);

            // Purchase #1: 500 shares × $200 = $100,000 (highest-value buy)
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = stockAaplId,
                    InsiderOwnerId = buyer.Id,
                    TransactionDate = today.AddDays(-5),
                    FilingDate = today.AddDays(-3),
                    TransactionCode = TransactionCode.Purchase,
                    Shares = 500,
                    PricePerShare = 200m,
                    AcquiredDisposed = AcquiredDisposed.Acquired,
                    SharesOwnedAfter = 10_500,
                    OwnershipNature = OwnershipNature.Direct,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0001-00-000001",
                    TransactionOrder = 0,
                }
            );

            // Purchase #2: 1,000 shares × $50 = $50,000 (more shares, less value)
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = stockMsftId,
                    InsiderOwnerId = buyer.Id,
                    TransactionDate = today.AddDays(-10),
                    FilingDate = today.AddDays(-8),
                    TransactionCode = TransactionCode.Purchase,
                    Shares = 1_000,
                    PricePerShare = 50m,
                    AcquiredDisposed = AcquiredDisposed.Acquired,
                    SharesOwnedAfter = 11_000,
                    OwnershipNature = OwnershipNature.Direct,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0001-00-000002",
                    TransactionOrder = 0,
                }
            );

            // Sale #1: 2,000 shares × $150 = $300,000 (highest-value overall)
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = stockAaplId,
                    InsiderOwnerId = seller.Id,
                    TransactionDate = today.AddDays(-2),
                    FilingDate = today.AddDays(-1),
                    TransactionCode = TransactionCode.Sale,
                    Shares = 2_000,
                    PricePerShare = 150m,
                    AcquiredDisposed = AcquiredDisposed.Disposed,
                    SharesOwnedAfter = 8_000,
                    OwnershipNature = OwnershipNature.Direct,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0001-00-000003",
                    TransactionOrder = 0,
                }
            );

            // Sale #2: 300 shares × $100 = $30,000 (lowest value)
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = stockMsftId,
                    InsiderOwnerId = seller.Id,
                    TransactionDate = today.AddDays(-15),
                    FilingDate = today.AddDays(-13),
                    TransactionCode = TransactionCode.Sale,
                    Shares = 300,
                    PricePerShare = 100m,
                    AcquiredDisposed = AcquiredDisposed.Disposed,
                    SharesOwnedAfter = 7_700,
                    OwnershipNature = OwnershipNature.Direct,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0001-00-000004",
                    TransactionOrder = 0,
                }
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/insider-trading/dashboard");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Empty state must NOT be shown
        await Assertions
            .Expect(
                page.Locator("h2").Filter(new() { HasTextString = "No insider transactions yet" })
            )
            .ToHaveCountAsync(0);

        // --- Top Buys ---
        var buysCard = page.Locator("[data-testid='insider-top-buys']");
        await Assertions.Expect(buysCard).ToHaveCountAsync(1);

        var buyRows = buysCard.Locator("table tbody tr");
        await Assertions.Expect(buyRows).ToHaveCountAsync(2);

        // First buy row must be the highest-value purchase ($100,000 AAPL),
        // not the one with more shares ($50,000 MSFT).
        await Assertions
            .Expect(buyRows.First.Locator("td").Nth(0))
            .ToContainTextAsync("Alice Buyer");
        await Assertions.Expect(buyRows.First.Locator("td").Nth(1)).ToContainTextAsync("AAPL");
        await Assertions
            .Expect(buyRows.First.Locator("td").Nth(2))
            .ToHaveTextAsync(new Regex(@"\$100.000"));

        // --- Top Sells ---
        var sellsCard = page.Locator("[data-testid='insider-top-sells']");
        await Assertions.Expect(sellsCard).ToHaveCountAsync(1);

        var sellRows = sellsCard.Locator("table tbody tr");
        await Assertions.Expect(sellRows).ToHaveCountAsync(2);

        // First sell row must be the highest-value sale ($300,000 AAPL).
        await Assertions
            .Expect(sellRows.First.Locator("td").Nth(0))
            .ToContainTextAsync("Bob Seller");
        await Assertions.Expect(sellRows.First.Locator("td").Nth(1)).ToContainTextAsync("AAPL");
        await Assertions
            .Expect(sellRows.First.Locator("td").Nth(2))
            .ToHaveTextAsync(new Regex(@"\$300.000"));

        // --- Biggest Transactions ---
        var biggestCard = page.Locator("[data-testid='insider-biggest']");
        await Assertions.Expect(biggestCard).ToHaveCountAsync(1);

        var biggestRows = biggestCard.Locator("table tbody tr");
        await Assertions.Expect(biggestRows).ToHaveCountAsync(4);

        // Overall highest-value is the $300,000 sale, proves BiggestTransactions
        // merges both types and orders by value across the board.
        await Assertions
            .Expect(biggestRows.First.Locator("td").Nth(0))
            .ToContainTextAsync("Bob Seller");
        await Assertions.Expect(biggestRows.First.Locator("td").Nth(1)).ToContainTextAsync("AAPL");
        await Assertions
            .Expect(biggestRows.First.Locator("td").Nth(5))
            .ToHaveTextAsync(new Regex(@"\$300.000"));
    }
}
