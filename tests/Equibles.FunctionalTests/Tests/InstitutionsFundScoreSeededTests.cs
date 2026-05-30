using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class InstitutionsFundScoreSeededTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public InstitutionsFundScoreSeededTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task AlphaSort_RanksByAlphaAndProfileShowsPerformanceCard()
    {
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);
        const string winnerCik = "0001000001";

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

            var winner = new InstitutionalHolder { Cik = winnerCik, Name = "Aaa Alpha Fund" };
            var laggard = new InstitutionalHolder { Cik = "0001000002", Name = "Aaa Beta Fund" };
            db.AddRange(winner, laggard);
            db.AddRange(
                MakeHolding(stockId, winner.Id, reportDate),
                MakeHolding(stockId, laggard.Id, reportDate),
                MakeScore(winner.Id, 12.3m),
                MakeScore(laggard.Id, -4.5m)
            );

            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/institutions?sort=AlphaDescending");
        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // The alpha column renders both funds' values; the +12.3% fund ranks above the -4.5% one.
        var rows = page.Locator("[data-testid='institutions-table'] tbody tr");
        await Assertions.Expect(rows.First).ToContainTextAsync("Aaa Alpha Fund");
        await Assertions.Expect(rows.Nth(1)).ToContainTextAsync("Aaa Beta Fund");
        await Assertions
            .Expect(rows.First.Locator("[data-testid='institution-alpha']"))
            .ToContainTextAsync("12.3%");

        // Open the top fund's profile and confirm the performance card with its 3-year alpha.
        await page.Locator($"a[href='/institutions/{winnerCik}']").First.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var card = page.Locator("[data-testid='institution-fund-score']");
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card).ToContainTextAsync("3-year performance vs SPY");
        await Assertions
            .Expect(card.Locator("[data-testid='fund-score-alpha']"))
            .ToContainTextAsync("12.30%");
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Value = 5_000_000,
            Shares = 1_000,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };

    private static FundScore MakeScore(Guid holderId, decimal alpha) =>
        new()
        {
            InstitutionalHolderId = holderId,
            BenchmarkTicker = "SPY",
            WindowYears = 3,
            WindowStart = new DateOnly(2021, 12, 31),
            WindowEnd = new DateOnly(2024, 12, 31),
            PortfolioCagrPercent = alpha + 8m,
            BenchmarkCagrPercent = 8m,
            PortfolioTotalReturnPercent = (alpha + 8m) * 3m,
            BenchmarkTotalReturnPercent = 24m,
            AlphaPercent = alpha,
            MaxDrawdownPercent = 15m,
        };
}
