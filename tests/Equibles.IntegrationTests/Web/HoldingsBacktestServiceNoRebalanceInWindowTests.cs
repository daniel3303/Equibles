using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Companion to HoldingsBacktestServiceNoSnapshotsTests (PR #2194) and the
/// happy-path render pins. The "filings exist but none rebalance inside the
/// requested window" branch is structurally distinct: the holder DOES have
/// 13F filings on file, but every filing's rebalance date (ReportDate + 45 days)
/// falls after the requested `to`. SelectRelevantSnapshotDates returns an
/// empty list and Execute must surface "No 13F snapshot's rebalance date falls
/// inside the requested window." — refusing to mark-to-market against an empty
/// portfolio rather than silently rendering a flat baseline.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsBacktestServiceNoRebalanceInWindowTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsBacktestServiceNoRebalanceInWindowTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task GetBacktest_AllFilingsRebalanceAfterTo_RendersNoRebalanceInWindowReason()
    {
        var holderCik = "0002000097";
        var holderId = Guid.NewGuid();
        var aaplId = Guid.NewGuid();
        var spyId = Guid.NewGuid();

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
                    Name = "Late Rebalancer Capital",
                }
            );
            // ReportDate is far in the future relative to the requested window
            // (2026-01-01 + 45 days rebalance = 2026-02-15, well past to=2024-12-31).
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = aaplId,
                    InstitutionalHolderId = holderId,
                    ReportDate = new DateOnly(2026, 1, 1),
                    FilingDate = new DateOnly(2026, 2, 15),
                    Shares = 10_000,
                    Value = 1_500_000,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "0001-future",
                }
            );
            // Seed a couple of benchmark prices so the BenchmarkNotFound arm
            // doesn't fire — the relevance branch must be the one that hits.
            db.Add(MakePrice(spyId, new DateOnly(2024, 6, 1), 400m));
            db.Add(MakePrice(spyId, new DateOnly(2024, 12, 1), 410m));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Institutions/{holderCik}/Backtest?from=2024-01-01&to=2024-12-31"
        );
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        html.Should().Contain("data-testid=\"backtest-reason\"");
        html.Should()
            .Contain("No 13F snapshot&#x27;s rebalance date falls inside the requested window.");
        html.Should().NotContain("data-testid=\"backtest-portfolio-summary\"");
    }

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
