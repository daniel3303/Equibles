using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorWindowAfterRebalanceMostRecentSnapshotTests
{
    private static readonly Guid StockOld = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StockNew = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Calculate_WindowAfterAllRebalances_OpensWithMostRecentSnapshotNotEarliest()
    {
        // Contract (Calculate source comment lines 52-54): "First snapshot whose rebalance
        // date is on or after `from`. If none — the window sits after the latest filing;
        // fall back to the MOST RECENT prior snapshot so the simulation still has a
        // portfolio to mark to market." The sibling
        // `Calculate_WindowStartsAfterOnlyRebalance_FallsBackToPriorSnapshotAndClampsStartDate`
        // pins the fallback with a SINGLE snapshot, so it can't distinguish "uses the
        // latest snapshot" from "uses any snapshot" — `ordered.Count - 1 == 0` collapses
        // both interpretations to the same result. This pin uses TWO snapshots that hold
        // DIFFERENT stocks, then drives only the later stock's price up across the window
        // so the portfolio value tracks the later snapshot's holdings. A regression to
        // `firstIdx < 0 ? 0 : firstIdx` (or `ordered.First()`-equivalent) would compile,
        // pass every existing single-snapshot test, and on the dominant production path
        // — a user opening the backtest page after the latest 13F has fully rebalanced —
        // silently simulate the ANCIENT portfolio instead of the recent one.
        var oldReportDate = new DateOnly(2022, 12, 31);
        var newReportDate = new DateOnly(2023, 12, 31);
        var newRebalance = newReportDate.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
        // Window is entirely AFTER both rebalances.
        var fromDate = newRebalance.AddDays(60);
        var toDate = fromDate.AddDays(10);

        var oldSnapshot = SingleStockSnapshot(oldReportDate, StockOld, 1_000_000);
        var newSnapshot = SingleStockSnapshot(newReportDate, StockNew, 1_000_000);

        // Old stock price stays flat at 100; new stock price doubles from day-1 onward.
        // If the simulation opens with the OLD snapshot, portfolio stays at 100.
        // If it opens with the NEW snapshot (the contract), portfolio doubles to 200.
        var result = HoldingsBacktestCalculator.Calculate(
            [oldSnapshot, newSnapshot],
            from: fromDate,
            to: toDate,
            priceOf: (stockId, day) =>
            {
                if (stockId == StockOld)
                    return 100m;
                return day == fromDate ? 100m : 200m;
            },
            benchmarkPriceOf: _ => 50m
        );

        result.Reason.Should().BeNullOrEmpty();
        result.Points.Should().NotBeEmpty();
        result.Points[^1].PortfolioValue.Should().Be(200m);
    }

    private static BacktestQuarterSnapshot SingleStockSnapshot(
        DateOnly reportDate,
        Guid stockId,
        long value
    ) =>
        new()
        {
            ReportDate = reportDate,
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = stockId,
                    Shares = 10_000,
                    Value = value,
                    IsOption = false,
                },
            },
        };
}
