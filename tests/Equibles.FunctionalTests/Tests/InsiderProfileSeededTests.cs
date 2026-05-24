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
public class InsiderProfileSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InsiderProfileSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Insider_WithTransactions_RendersProfileAndTickerLinks()
    {
        // Pins the /insiders/{ownerCik} route — heading, role/location metadata,
        // transactions table, and the asp-action/asp-controller links on ticker
        // cells that resolve to /stocks/{ticker}.
        var stockId = Guid.NewGuid();
        var ownerCik = "0001234567";

        await _web.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Id = stockId,
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            };
            db.Add(stock);

            var owner = new InsiderOwner
            {
                OwnerCik = ownerCik,
                Name = "Jane TestInsider",
                City = "Cupertino",
                StateOrCountry = "CA",
                IsOfficer = true,
                OfficerTitle = "CEO",
            };
            db.Add(owner);

            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = stockId,
                    InsiderOwnerId = owner.Id,
                    TransactionDate = new DateOnly(2025, 3, 15),
                    FilingDate = new DateOnly(2025, 3, 17),
                    TransactionCode = TransactionCode.Purchase,
                    Shares = 1_000,
                    PricePerShare = 175.50m,
                    AcquiredDisposed = AcquiredDisposed.Acquired,
                    SharesOwnedAfter = 11_000,
                    OwnershipNature = OwnershipNature.Direct,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0001-00-000099",
                    TransactionOrder = 0,
                }
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/insiders/{ownerCik}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Jane TestInsider");
        await Assertions.Expect(page.Locator("text=CIK " + ownerCik)).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("text=CEO")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("text=Cupertino, CA")).ToHaveCountAsync(1);

        var rows = page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(1);

        var tickerLink = rows.First.Locator("a").First;
        await Assertions.Expect(tickerLink).ToHaveTextAsync("AAPL");
        var href = await tickerLink.GetAttributeAsync("href");
        href.Should()
            .StartWith(
                "/stocks",
                "ticker link uses asp-action/asp-controller; a broken tag helper renders href=\"\""
            );

        await Assertions.Expect(rows.First).ToContainTextAsync(new Regex(@"1.000"));
        await Assertions.Expect(rows.First).ToContainTextAsync(new Regex(@"\$175.50"));
    }
}
