using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorCagrAnnualizationBoundaryTests
{
    private static readonly Guid Stock = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // Contract (273fcdd7): CAGR is suppressed only for windows SHORTER than
    // MinAnnualizationDays (90) — a window spanning exactly 90 days is not shorter,
    // so it must report the annualized rate: ((110/100)^(365.25/90) − 1) × 100 ≈ 47.23%.
    [Fact]
    public void Calculate_WindowSpansExactlyMinAnnualizationDays_ReportsAnnualizedCagr()
    {
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = new DateOnly(2024, 3, 31),
            Positions =
            {
                new BacktestPosition
                {
                    CommonStockId = Stock,
                    Shares = 1_000,
                    Value = 100_000,
                    IsOption = false,
                },
            },
        };
        // ReportDate + RebalanceDelayDays(45) = 2024-05-15; opening the window there makes
        // the simulated series span exactly MinAnnualizationDays from first to last point.
        var from = new DateOnly(2024, 5, 15);
        var to = from.AddDays(HoldingsBacktestCalculator.MinAnnualizationDays);

        var result = HoldingsBacktestCalculator.Calculate(
            [snapshot],
            from,
            to,
            priceOf: (_, day) => day == from ? 100m : 110m,
            benchmarkPriceOf: _ => 100m
        );

        result.PortfolioSummary.TotalReturnPercent.Should().Be(10m);
        result.PortfolioSummary.CagrPercent.Should().NotBeNull();
        result.PortfolioSummary.CagrPercent.Should().BeApproximately(47.23m, 0.05m);
    }
}
