using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorComputeCagrOverflowSaturatesTests
{
    [Fact]
    public void ComputeCagr_ExtremeRatioOverMinAnnualizableWindow_ReturnsDecimalMaxValue()
    {
        // Source contract (HoldingsBacktestCalculator.ComputeCagr): extreme moves
        // drive the annualised compounding past decimal.MaxValue; saturate the
        // CAGR cell rather than aborting the calculation — TotalReturn% /
        // MaxDrawdown% are still meaningful. A refactor that drops the try/catch
        // on (decimal)compounded — perhaps reasoning that "no one throws here" —
        // would crash on any extreme ratio. Sub-MinAnnualizationDays windows now
        // return null instead of annualizing, so the overflow needs a huge ratio
        // over the minimum annualizable window: ratio = 1e8 over 90 days gives
        // (1e8)^(365.25/90) ≈ 1e32.5 — well past decimal.MaxValue (~7.9e28), so
        // the cast throws and the catch must saturate to decimal.MaxValue.
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "ComputeCagr",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (decimal?)
            method.Invoke(
                null,
                [100m, 10_000_000_000m, HoldingsBacktestCalculator.MinAnnualizationDays]
            );

        result.Should().Be(decimal.MaxValue);
    }
}
