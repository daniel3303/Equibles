using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorWindowAfterRebalanceTests
{
    private static readonly Guid StockA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Calculate_WindowStartsAfterOnlyRebalance_FallsBackToPriorSnapshotAndClampsStartDate()
    {
        // Contract (Calculate XML doc): "First snapshot whose rebalance date is on or after
        // `from`. If none — the window sits after the latest filing; fall back to the most
        // recent prior snapshot so the simulation still has a portfolio to mark to market."
        // No existing test exercises this fallback path (`firstIdx == -1`) with the
        // startDate < from clamp, where the simulation opens with the prior portfolio at
        // the requested `from`. Pin the clamp + the populated mark-to-market.
        var reportDate = new DateOnly(2024, 3, 31);
        var rebalanceDate = reportDate.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
        var fromDate = rebalanceDate.AddDays(30); // entire window is AFTER the only rebalance
        var toDate = fromDate.AddDays(5);

        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = reportDate,
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = StockA,
                    Shares = 10_000,
                    Value = 1_000_000,
                    IsOption = false,
                },
            },
        };

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: fromDate,
            to: toDate,
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        result
            .Reason.Should()
            .BeNullOrEmpty("the prior-snapshot fallback must let the simulation proceed");
        result
            .StartDate.Should()
            .Be(fromDate, "startDate is clamped from the older rebalance up to `from`");
        result.Points.Should().NotBeEmpty();
        result.Points[0].Date.Should().Be(fromDate);
        result.Points[^1].Date.Should().Be(toDate);
    }
}
