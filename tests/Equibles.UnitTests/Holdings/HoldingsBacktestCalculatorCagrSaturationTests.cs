using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorCagrSaturationTests
{
    [Fact]
    public void Calculate_OneDayWindowWithDoublingPortfolio_CagrSaturatesToDecimalMaxValue()
    {
        // Contract from the GH-1199 fix: "The CAGR cell may be saturated / clamped /
        // decimal.MaxValue, but the call must not throw." The existing regression
        // test (CagrShortWindowTests) only asserts non-throw and a populated summary;
        // pin the actual saturation VALUE so a future tweak to the catch block
        // (e.g. swapping to a different sentinel) is caught.
        var stockId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var rebalanceDate = new DateOnly(2025, 1, 1);
        var reportDate = rebalanceDate.AddDays(-HoldingsBacktestCalculator.RebalanceDelayDays);

        var snapshot = new BacktestQuarterSnapshot
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

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from: rebalanceDate,
            to: rebalanceDate.AddDays(1),
            priceOf: (_, day) => day == rebalanceDate ? 100m : 200m,
            benchmarkPriceOf: day => day == rebalanceDate ? 100m : 200m
        );

        result.PortfolioSummary.CagrPercent.Should().Be(decimal.MaxValue);
    }
}
