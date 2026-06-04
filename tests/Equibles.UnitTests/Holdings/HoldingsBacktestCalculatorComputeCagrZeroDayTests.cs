using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorComputeCagrZeroDayTests
{
    [Fact]
    public void ComputeCagr_ZeroDayWindow_ReturnsNull()
    {
        // Contract (HoldingsBacktestCalculator.ComputeCagr): windows shorter than
        // MinAnnualizationDays — including the degenerate dayCount == 0 — report
        // no CAGR at all (null). The boundary matters: letting a same-day window
        // through means years = 0 / 365.25 = 0, and Math.Pow(ratio, 1.0 / 0)
        // yields PositiveInfinity (ratio > 1), overflowing the decimal cast and
        // saturating the cell to decimal.MaxValue instead of reporting an honest
        // "no time elapsed → no annualised rate". Pinning the boundary protects
        // the guard from drifting.
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "ComputeCagr",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (decimal?)method!.Invoke(null, [100m, 200m, 0]);

        result.Should().BeNull();
    }
}
