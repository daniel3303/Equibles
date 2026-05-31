using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionsBacktestReportDateOverflowTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionsBacktestReportDateOverflowTests(
        WebAppFixture web,
        PlaywrightFixture playwright
    )
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Backtest_HolderWithFarFutureReportDate_DoesNotReturn500()
    {
        // Contract: the backtest page must respond gracefully for any holder that has 13F
        // snapshots, whatever their stored report dates. The shift to the rebalance date
        // (ReportDate + 45 days) must not overflow the calendar. HoldingsBacktestCalculator
        // already clamps this exact arithmetic (DayNumber guard / DateOnly.MaxValue), so a
        // snapshot whose ReportDate is within 45 days of DateOnly.MaxValue should yield a
        // "no data" page, never an HTTP 500.
        var stockId = Guid.NewGuid();
        var filerCik = "0001067983";

        await _web.ResetAndSeedAsync(async db =>
        {
            // Benchmark stock must exist so Execute proceeds past the benchmark lookup and
            // reaches the rebalance-date computation.
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "SPY",
                    Name = "SPDR S&P 500 ETF Trust",
                    Cik = "0000884394",
                }
            );

            var filer = new InstitutionalHolder { Cik = filerCik, Name = "Berkshire Hathaway Inc" };
            db.Add(filer);

            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    InstitutionalHolderId = filer.Id,
                    // ReportDate within 45 days of DateOnly.MaxValue: ReportDate.AddDays(45)
                    // pushes past 9999-12-31 and throws ArgumentOutOfRangeException unless clamped.
                    ReportDate = DateOnly.MaxValue,
                    FilingDate = new DateOnly(2025, 2, 14),
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
        response!.Status.Should().NotBe(500);
    }
}
