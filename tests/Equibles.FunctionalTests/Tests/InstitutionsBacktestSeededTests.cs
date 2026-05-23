using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using Equibles.Yahoo.Data.Models;
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
    public async Task BacktestInstitution_WithHoldingsAndPrices_RendersSummaryCards()
    {
        // Seed a holder with one quarter of holdings plus daily prices for the held stock
        // and the default benchmark (SPY). The calculator needs prices spanning the rebalance
        // window (ReportDate + 45 days) to produce points and summary stats.
        var aaplId = Guid.NewGuid();
        var spyId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 9, 30);
        var holderCik = "0001067983";

        await _web.ResetAndSeedAsync(async db =>
        {
            var aapl = new CommonStock
            {
                Id = aaplId,
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            };
            var spy = new CommonStock
            {
                Id = spyId,
                Ticker = "SPY",
                Name = "SPDR S&P 500 ETF Trust",
                Cik = "0000884394",
            };
            db.AddRange(aapl, spy);

            var holder = new InstitutionalHolder { Cik = holderCik, Name = "Test Fund" };
            db.Add(holder);

            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = aaplId,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = reportDate,
                    FilingDate = reportDate.AddDays(45),
                    Value = 100_000,
                    Shares = 500,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                }
            );

            // Seed daily prices for both AAPL and SPY covering the simulation window.
            // The rebalance date is ReportDate + 45 = 2024-11-14. Prices must span from
            // before that date through at least a few weeks after for the chart to render.
            var priceStart = new DateOnly(2024, 11, 1);
            for (var i = 0; i < 60; i++)
            {
                var date = priceStart.AddDays(i);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                db.Add(
                    new DailyStockPrice
                    {
                        CommonStockId = aaplId,
                        Date = date,
                        Open = 200m + i,
                        High = 202m + i,
                        Low = 198m + i,
                        Close = 201m + i,
                        AdjustedClose = 201m + i,
                        Volume = 50_000_000,
                    }
                );
                db.Add(
                    new DailyStockPrice
                    {
                        CommonStockId = spyId,
                        Date = date,
                        Open = 550m + i,
                        High = 552m + i,
                        Low = 548m + i,
                        Close = 551m + i,
                        AdjustedClose = 551m + i,
                        Volume = 80_000_000,
                    }
                );
            }

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/institutions/{holderCik}/backtest");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Heading renders the holder name.
        var heading = page.Locator("[data-testid='backtest-heading']");
        await Assertions.Expect(heading).ToContainTextAsync("Test Fund");

        // Filter form renders with benchmark selector.
        var filters = page.Locator("[data-testid='backtest-filters']");
        await Assertions.Expect(filters).ToHaveCountAsync(1);
        await Assertions.Expect(filters.Locator("select[name='benchmark']")).ToHaveCountAsync(1);

        // Portfolio and benchmark summary cards render with stats.
        var portfolioSummary = page.Locator("[data-testid='backtest-portfolio-summary']");
        await Assertions.Expect(portfolioSummary).ToHaveCountAsync(1);
        await Assertions.Expect(portfolioSummary).ToContainTextAsync("Total return");
        await Assertions.Expect(portfolioSummary).ToContainTextAsync("CAGR");
        await Assertions.Expect(portfolioSummary).ToContainTextAsync("Max drawdown");

        var benchmarkSummary = page.Locator("[data-testid='backtest-benchmark-summary']");
        await Assertions.Expect(benchmarkSummary).ToHaveCountAsync(1);
        await Assertions.Expect(benchmarkSummary).ToContainTextAsync("SPY");

        // Chart card renders.
        var chartCard = page.Locator("[data-testid='backtest-chart-card']");
        await Assertions.Expect(chartCard).ToHaveCountAsync(1);
        await Assertions.Expect(chartCard).ToContainTextAsync("Cumulative return");
    }

    [Fact]
    public async Task BacktestInstitution_UnknownCik_Returns404()
    {
        await _web.ResetAndSeedAsync();

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/institutions/0009999999/backtest");

        response.Should().NotBeNull();
        response!.Status.Should().Be(404);
    }
}
