using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorWindowBetweenTwoRebalancesTests
{
    private static readonly Guid StockOld = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StockNew = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact(
        Skip = "GH-3193 — Calculate skips the active prior snapshot when `from` falls between two rebalance dates"
    )]
    public void Calculate_FromBetweenTwoRebalances_OpensAtRequestedFromWithPriorSnapshot()
    {
        // Contract: HoldingsBacktestService.SelectRelevantSnapshotDates deliberately feeds the
        // latest snapshot whose rebalance precedes `from` "so the simulation can open with an
        // initial portfolio" — i.e. the simulation must open at the requested `from`, marking the
        // active (prior) portfolio to market until the next rebalance matures. The sibling
        // single-snapshot pin (WindowAfterRebalanceTests) already asserts StartDate == from for
        // exactly that prior-portfolio open. Adding ONE later snapshot whose rebalance falls
        // inside the window must not change where the window opens. Here `from` sits strictly
        // between the old snapshot's rebalance and the new snapshot's rebalance.
        var oldReportDate = new DateOnly(2023, 12, 31);
        var newReportDate = new DateOnly(2024, 3, 31);
        var oldRebalance = oldReportDate.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
        var newRebalance = newReportDate.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
        var fromDate = oldRebalance.AddDays(30); // strictly between the two rebalance dates
        var toDate = newRebalance.AddDays(30); // window also spans the later rebalance

        var oldSnapshot = SingleStockSnapshot(oldReportDate, StockOld);
        var newSnapshot = SingleStockSnapshot(newReportDate, StockNew);

        var result = HoldingsBacktestCalculator.Calculate(
            [oldSnapshot, newSnapshot],
            from: fromDate,
            to: toDate,
            priceOf: (_, _) => 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.Reason.Should().BeNullOrEmpty();
        result.StartDate.Should().Be(fromDate);
        result.Points[0].Date.Should().Be(fromDate);
    }

    private static BacktestQuarterSnapshot SingleStockSnapshot(DateOnly reportDate, Guid stockId) =>
        new()
        {
            ReportDate = reportDate,
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = stockId,
                    Shares = 10_000,
                    Value = 1_000_000,
                    IsOption = false,
                },
            },
        };
}
