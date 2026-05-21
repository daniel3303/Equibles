using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorComputeCagrOverflowSaturatesTests
{
    [Fact]
    public void ComputeCagr_DoublingOverOneDayWindow_ReturnsDecimalMaxValue()
    {
        // Source contract (HoldingsBacktestCalculator.cs:205-208): "Extreme
        // single-window moves (e.g. an intraday double on a one-day window)
        // drive the annualised compounding past decimal.MaxValue; saturate
        // the CAGR cell rather than aborting the calculation — TotalReturn%
        // / MaxDrawdown% are still meaningful." A refactor that drops the
        // try/catch on (decimal)compounded — perhaps reasoning that "no one
        // throws here" — would crash on any single-day window where the
        // portfolio at least doubles, because 2^365.25 overflows decimal.
        // ratio = final/initial = 2; years = 1/365.25 ≈ 0.00274;
        // compounded = 2^(1/years) - 1 ≈ 2^365.25 - 1 ≈ 7.5e109 — well
        // past decimal.MaxValue (~7.9e28), so the cast throws and the
        // catch must saturate to decimal.MaxValue.
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "ComputeCagr",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (decimal)method.Invoke(null, [100m, 200m, 1]);

        result.Should().Be(decimal.MaxValue);
    }
}
