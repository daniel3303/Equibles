using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

// Lane B: pins the early-return when the nearest rebalance date falls after `to`,
// covering the previously zero-hit branch at Calculate lines 69-72.
public class HoldingsBacktestCalculatorRebalanceAfterWindowTests
{
    private static readonly Guid StockA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Calculate_NearestRebalanceAfterTo_ReturnsReasonString()
    {
        // Snapshot report date → rebalance = report + 45 days.
        // Window [from, to] ends before that rebalance.
        var reportDate = new DateOnly(2024, 5, 1);
        var rebalanceDate = reportDate.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
        var fromDate = rebalanceDate.AddDays(-10);
        var toDate = rebalanceDate.AddDays(-1);

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

        result.Points.Should().BeEmpty();
        result.Reason.Should().Contain("no rebalance date falls inside the requested window");
    }
}
