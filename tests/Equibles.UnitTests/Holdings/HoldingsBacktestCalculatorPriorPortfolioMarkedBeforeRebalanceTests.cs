using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorPriorPortfolioMarkedBeforeRebalanceTests
{
    private static readonly Guid StockOld = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StockNew = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // When `from` sits strictly between two rebalance dates, opening at `from` is only
    // correct if the simulation actually holds the PRIOR snapshot there and marks it to
    // market until the next rebalance matures. The sibling WindowBetweenTwoRebalances pin
    // only checks the open DATE; this pins the open PORTFOLIO. StockOld (the prior snapshot)
    // doubles between `from` and the new rebalance while StockNew stays flat, so a portfolio
    // opened 100% in StockOld must read $200 on the pre-rebalance day. With the old
    // snapshot-selection bug the window opened at the later rebalance holding StockNew, so
    // that day was never simulated and StockOld's move could not show up.
    [Fact]
    public void Calculate_FromBetweenTwoRebalances_HoldsPriorSnapshotAndMarksItToMarket()
    {
        var oldReportDate = new DateOnly(2023, 12, 31);
        var newReportDate = new DateOnly(2024, 3, 31);
        var oldRebalance = oldReportDate.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
        var newRebalance = newReportDate.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
        var fromDate = oldRebalance.AddDays(30); // strictly between the two rebalance dates
        var preRebalanceDay = fromDate.AddDays(10); // still before newRebalance
        var toDate = newRebalance.AddDays(30);

        var result = HoldingsBacktestCalculator.Calculate(
            [
                SingleStockSnapshot(oldReportDate, StockOld),
                SingleStockSnapshot(newReportDate, StockNew),
            ],
            from: fromDate,
            to: toDate,
            // StockOld doubles on preRebalanceDay; StockNew is flat. A portfolio holding the
            // prior snapshot (StockOld) reads $200 that day — initial $100 fully in StockOld.
            priceOf: (stockId, day) => stockId == StockOld && day >= preRebalanceDay ? 200m : 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.Reason.Should().BeNullOrEmpty();
        result
            .Points.Single(p => p.Date == preRebalanceDay)
            .PortfolioValue.Should()
            .Be(200m, "the prior snapshot (StockOld) is held from `from` and marked to market");
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
