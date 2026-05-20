using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the new /Institutions/{cik}/Backtest route end-to-end: route → controller →
/// HoldingsBacktestService → calculator → view. Asserts the page renders the heading,
/// filter form, summary cards, and the slice-2 daily-series table.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesInstitutionBacktestTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesInstitutionBacktestTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetBacktest_UnknownHolder_Returns404()
    {
        await _fixture.ResetAndSeedAsync();

        var response = await _fixture.Client.GetAsync("/Institutions/0009999999/Backtest");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBacktest_HolderWithoutBenchmark_RendersBenchmarkNotFoundAlert()
    {
        var holderCik = "0002000001";
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new InstitutionalHolder { Cik = holderCik, Name = "No Benchmark Capital" });
            await Task.CompletedTask;
        });

        // SPY isn't seeded — service should flag the benchmark as missing.
        var response = await _fixture.Client.GetAsync($"/Institutions/{holderCik}/Backtest");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        html.Should().Contain("data-testid=\"backtest-heading\"");
        html.Should().Contain("data-testid=\"backtest-benchmark-error\"");
    }

    [Fact]
    public async Task GetBacktest_HolderWithSnapshotsAndPrices_RendersSummaryCardsAndSeriesTable()
    {
        var holderCik = "0002000002";
        var holderId = Guid.NewGuid();
        var aaplId = Guid.NewGuid();
        var spyId = Guid.NewGuid();
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var rebalanceQ1 = q1.AddDays(45);
        var rebalanceQ2 = q2.AddDays(45);

        await _fixture.ResetAndSeedAsync(async db =>
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
                    Id = spyId,
                    Ticker = "SPY",
                    Name = "SPDR S&P 500 ETF",
                    Cik = "0000884394",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = holderCik,
                    Name = "Backtest Capital",
                }
            );
            db.Add(MakeHolding(aaplId, holderId, q1, shares: 10_000, value: 1_000_000));
            db.Add(MakeHolding(aaplId, holderId, q2, shares: 10_000, value: 1_500_000));

            // Daily prices — flat $100 AAPL, flat $400 SPY across both rebalance windows.
            // Forward-fill on the rebalance dates resolves to these closes.
            for (var d = rebalanceQ1.AddDays(-7); d <= rebalanceQ2.AddDays(7); d = d.AddDays(1))
            {
                db.Add(MakePrice(aaplId, d, 100m));
                db.Add(MakePrice(spyId, d, 400m));
            }
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Institutions/{holderCik}/Backtest?from={rebalanceQ1:yyyy-MM-dd}&to={rebalanceQ2.AddDays(7):yyyy-MM-dd}"
        );
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        html.Should().Contain("data-testid=\"backtest-heading\"");
        html.Should().Contain("Backtest Capital");
        html.Should().Contain("data-testid=\"backtest-filters\"");
        html.Should().Contain("data-testid=\"backtest-portfolio-summary\"");
        html.Should().Contain("data-testid=\"backtest-benchmark-summary\"");
        html.Should().Contain("data-testid=\"backtest-series-table\"");
        html.Should().Contain("Total return");
        html.Should().Contain("Max drawdown");
        // Flat prices → both series stay at the initial 100 normalized value. Allow either
        // culture's decimal separator (`100.00` US, `100,00` ES/PT/FR) so the test passes
        // regardless of the host culture.
        var hasInitialPoint = html.Contains("100.00") || html.Contains("100,00");
        hasInitialPoint.Should().BeTrue(html);
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
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };

    private static DailyStockPrice MakePrice(Guid stockId, DateOnly date, decimal close) =>
        new()
        {
            CommonStockId = stockId,
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = close,
            Volume = 1_000_000,
        };
}
