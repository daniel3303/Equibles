using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorCagrSaturationTests
{
    [Fact]
    public void Calculate_ExtremeMoveOverMinAnnualizableWindow_CagrSaturatesToDecimalMaxValue()
    {
        // Contract from the GH-1199 fix: "The CAGR cell may be saturated / clamped /
        // decimal.MaxValue, but the call must not throw." The existing regression
        // test (CagrShortWindowTests) only asserts non-throw and a populated summary;
        // pin the actual saturation VALUE so a future tweak to the catch block
        // (e.g. swapping to a different sentinel) is caught. Windows below
        // MinAnnualizationDays no longer annualize at all, so the overflow needs an
        // extreme price ratio (1e8 over the minimum annualizable window —
        // (1e8)^(365.25/90) far exceeds decimal.MaxValue ≈ 7.92e28).
        var stockId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var rebalanceDate = new DateOnly(2025, 1, 1);
        var reportDate = rebalanceDate.AddDays(-HoldingsBacktestCalculator.RebalanceDelayDays);
        var endDate = rebalanceDate.AddDays(HoldingsBacktestCalculator.MinAnnualizationDays);

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
            to: endDate,
            priceOf: (_, day) => day == endDate ? 10_000m : 0.0001m,
            benchmarkPriceOf: _ => 100m
        );

        result.PortfolioSummary.CagrPercent.Should().Be(decimal.MaxValue);
    }
}
