using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class CongressMemberProfileSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public CongressMemberProfileSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Member_WithTrades_RendersProfileAndTickerLinks()
    {
        // Pins the /congress/{id:guid} route — heading, "Member of Congress" subtitle,
        // trades table, and the asp-action/asp-controller links on ticker cells that
        // resolve to /stocks/{ticker}.
        var stockId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await _web.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Id = stockId,
                Ticker = "MSFT",
                Name = "Microsoft Corporation",
                Cik = "0000789019",
            };
            db.Add(stock);

            var member = new CongressMember
            {
                Id = memberId,
                Name = "Jane Q. Tester",
                Position = CongressPosition.Senator,
            };
            db.Add(member);

            db.Add(
                new CongressionalTrade
                {
                    CommonStockId = stockId,
                    CongressMemberId = memberId,
                    TransactionDate = new DateOnly(2025, 6, 10),
                    FilingDate = new DateOnly(2025, 7, 1),
                    TransactionType = CongressTransactionType.Purchase,
                    OwnerType = "Spouse",
                    AssetName = "Microsoft Corp [MSFT]",
                    AmountFrom = 15_001,
                    AmountTo = 50_000,
                }
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/congress/{memberId}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Jane Q. Tester");
        await Assertions.Expect(page.Locator("text=Member of Congress")).ToHaveCountAsync(1);

        var rows = page.Locator("table tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(1);

        var tickerLink = rows.First.Locator("a").First;
        await Assertions.Expect(tickerLink).ToHaveTextAsync("MSFT");
        var href = await tickerLink.GetAttributeAsync("href");
        href.Should()
            .StartWith(
                "/stocks",
                "ticker link uses asp-action/asp-controller; a broken tag helper renders href=\"\""
            );

        await Assertions.Expect(rows.First).ToContainTextAsync("Spouse");
        await Assertions.Expect(rows.First).ToContainTextAsync(new Regex(@"15.001"));
        await Assertions.Expect(rows.First).ToContainTextAsync(new Regex(@"50.000"));
    }
}
