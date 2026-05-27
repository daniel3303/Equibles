using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorComputeCagrZeroDayTests
{
    [Fact]
    public void ComputeCagr_ZeroDayWindow_ReturnsZero()
    {
        // Contract (HoldingsBacktestCalculator.cs:204-205): `dayCount <= 0`
        // short-circuits to 0m. The boundary dayCount == 0 is the failure mode
        // a regression most easily reintroduces — flipping the guard to
        // `dayCount < 0` lets a same-day window through. Then years = 0 / 365.25
        // = 0, and Math.Pow(ratio, 1.0 / 0) yields PositiveInfinity (ratio > 1)
        // or NaN (ratio == 1 → Pow(1, inf) == 1; PositiveInfinity - 1.0 still
        // overflows the decimal cast). Either way the CAGR cell saturates to
        // decimal.MaxValue (or throws if the catch is also dropped) instead of
        // reporting an honest "no time elapsed → no annualised rate". Pinning
        // the exact boundary protects this guard from a one-character drift.
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "ComputeCagr",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (decimal)method!.Invoke(null, [100m, 200m, 0]);

        result.Should().Be(0m);
    }
}
