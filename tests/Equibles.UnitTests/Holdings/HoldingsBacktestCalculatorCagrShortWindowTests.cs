using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorCagrShortWindowTests
{
    [Fact]
    public void Calculate_OneDayWindowWithDoublingPortfolio_ReturnsSummaryWithoutOverflow()
    {
        // Contract: Calculate returns a populated BacktestResult — including a
        // PortfolioSummary with TotalReturn / CAGR / MaxDrawdown — for any window
        // where there's a rebalance + benchmark price. An extreme intraday move
        // must not blow up the CAGR cast: Math.Pow(2.0, 365.25) ≈ 7.5e109, which
        // exceeds decimal.MaxValue (≈7.92e28); the calculator must clamp / guard
        // / round instead of throwing OverflowException.
        var stockId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var rebalanceDate = new DateOnly(2025, 1, 1);
        var reportDate = rebalanceDate.AddDays(-HoldingsBacktestCalculator.RebalanceDelayDays);
        var endDate = rebalanceDate.AddDays(1);

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

        var act = () =>
            HoldingsBacktestCalculator.Calculate(
                [snapshot],
                from: rebalanceDate,
                to: endDate,
                priceOf: (_, day) => day == rebalanceDate ? 100m : 200m,
                benchmarkPriceOf: day => day == rebalanceDate ? 100m : 200m
            );

        act.Should().NotThrow();
        var result = act();
        result.Points.Should().HaveCount(2);
        result.PortfolioSummary.Should().NotBeNull();
    }
}
