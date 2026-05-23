using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionsBacktestSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionsBacktestSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Backtest_ValidFiler_RendersHeadingAndFilterForm()
    {
        // Seed a filer with holdings so the backtest page can load.
        // No Yahoo price data is seeded, so the backtest will show a reason/warning
        // instead of a chart — but the heading, filter form, and page structure should render.
        var stockId = Guid.NewGuid();
        var filerCik = "0001067983";
        var reportDate = new DateOnly(2024, 12, 31);

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

            var filer = new InstitutionalHolder { Cik = filerCik, Name = "Berkshire Hathaway Inc" };
            db.Add(filer);

            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    InstitutionalHolderId = filer.Id,
                    ReportDate = reportDate,
                    FilingDate = reportDate.AddDays(45),
                    Value = 5_000_000,
                    Shares = 200,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                }
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/institutions/{filerCik}/backtest");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Heading should contain the filer name.
        var heading = page.Locator("[data-testid='backtest-heading']");
        await Assertions.Expect(heading).ToContainTextAsync("Berkshire Hathaway");

        // Filter form should render with date inputs and benchmark selector.
        var filters = page.Locator("[data-testid='backtest-filters']");
        await Assertions.Expect(filters).ToHaveCountAsync(1);
        await Assertions.Expect(filters.Locator("input[name='from']")).ToHaveCountAsync(1);
        await Assertions.Expect(filters.Locator("input[name='to']")).ToHaveCountAsync(1);
        await Assertions.Expect(filters.Locator("select[name='benchmark']")).ToHaveCountAsync(1);
        await Assertions.Expect(filters.Locator("button[type='submit']")).ToContainTextAsync("Run");

        // Without price data, the backtest has no points to plot. The page may show
        // a reason warning, a chart, or neither — all valid. The key assertion is that
        // the page didn't crash (200 OK) and the structure above rendered correctly.
    }
}
