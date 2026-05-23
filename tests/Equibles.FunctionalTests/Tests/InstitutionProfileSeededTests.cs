using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionProfileSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionProfileSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Institution_WithHoldings_RendersSummaryAndActionLinks()
    {
        // Seed a filer with holdings across two quarters so the profile shows
        // the portfolio summary, action links, and quarterly activity sections.
        var stockId = Guid.NewGuid();
        var filerCik = "0001067983";
        var prior = new DateOnly(2024, 9, 30);
        var latest = new DateOnly(2024, 12, 31);

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

            var filer = new InstitutionalHolder
            {
                Cik = filerCik,
                Name = "Berkshire Hathaway Inc",
                City = "Omaha",
                StateOrCountry = "NE",
            };
            db.Add(filer);

            db.Add(MakeHolding(stockId, filer.Id, prior, 100, 3_000_000));
            db.Add(MakeHolding(stockId, filer.Id, latest, 150, 5_000_000));

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/institutions/{filerCik}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Heading should show filer name and CIK.
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Berkshire Hathaway");
        await Assertions.Expect(page.Locator("text=CIK " + filerCik)).ToHaveCountAsync(1);

        // Portfolio summary card should render with stat labels.
        var summary = page.Locator("[data-testid='institution-summary']");
        await Assertions.Expect(summary).ToHaveCountAsync(1);
        await Assertions.Expect(summary).ToContainTextAsync("Reported AUM");
        await Assertions.Expect(summary).ToContainTextAsync("Positions");

        // Backtest and CSV links should be visible when holdings exist.
        await Assertions
            .Expect(page.Locator("[data-testid='institution-backtest-link']"))
            .ToBeVisibleAsync();
        await Assertions
            .Expect(page.Locator("[data-testid='institution-export-csv']"))
            .ToBeVisibleAsync();
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        long value
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
        };
}
