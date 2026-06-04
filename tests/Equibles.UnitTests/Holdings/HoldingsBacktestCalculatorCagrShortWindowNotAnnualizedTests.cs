using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorCagrShortWindowNotAnnualizedTests
{
    [Fact]
    public void Calculate_NineteenDayWindow_DoesNotReportAnAnnualizedCagr()
    {
        // GH (commercial) #1540: the Smart Money Index page annualized a 19-day
        // +8.75% return into a "CAGR" of (1.0875)^(365.25/19) - 1 ~= 448% — a
        // statistically meaningless extrapolation presented as a compounding
        // rate. Windows shorter than MinAnnualizationDays must report no CAGR
        // at all (null); total return and max drawdown stay populated, since
        // they are meaningful over any window.
        var stockId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var rebalanceDate = new DateOnly(2026, 5, 15);
        var reportDate = rebalanceDate.AddDays(-HoldingsBacktestCalculator.RebalanceDelayDays);
        var endDate = rebalanceDate.AddDays(19);

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
            priceOf: (_, day) => day == endDate ? 108.75m : 100m,
            benchmarkPriceOf: _ => 100m
        );

        result.PortfolioSummary.TotalReturnPercent.Should().BeApproximately(8.75m, 0.01m);
        result
            .PortfolioSummary.CagrPercent.Should()
            .BeNull("a 19-day window is too short to annualize into a meaningful CAGR");
        result
            .BenchmarkSummary.CagrPercent.Should()
            .BeNull("the benchmark spans the same 19-day window");
    }
}
