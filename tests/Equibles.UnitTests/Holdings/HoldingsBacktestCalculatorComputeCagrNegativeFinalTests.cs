using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorComputeCagrNegativeFinalTests
{
    [Fact]
    public void ComputeCagr_NegativeFinal_ReturnsZero()
    {
        // Contract (HoldingsBacktestCalculator.cs:204-205): `final <= 0`
        // short-circuits to 0m. The negative arm matters most: without the
        // guard, ratio = -50/100 = -0.5, Math.Pow on a negative base with a
        // fractional exponent returns NaN, then `(decimal)double.NaN` throws
        // OverflowException — which the surrounding try/catch saturates to
        // decimal.MaxValue. A refactor that loosens the guard to `final < 0`
        // (the boundary case == 0 falling through) would still hit the same
        // overflow funnel; either drop or tightening of the guard reports a
        // wiped-out portfolio as MAX gain instead of 0%. Direct invocation
        // pins the exact discriminator the guard exists to defend.
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "ComputeCagr",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (decimal)method!.Invoke(null, [100m, -50m, 365]);

        result.Should().Be(0m);
    }
}
