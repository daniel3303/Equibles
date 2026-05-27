using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Sibling to <c>Calculate_MaxDrawdown_CalculatedCorrectly</c>, whose series
/// (100 → 75 → 100) never exceeds the initial value — every drawdown is
/// measured against <c>values[0]</c>, so the running-peak update arm
/// (<c>if (v &gt; peak) peak = v</c>) never fires. A series that *climbs* first
/// and then drops requires that arm to be present; if a refactor "simplified"
/// it away the peak would stay frozen at <c>values[0]</c> and every drawdown
/// in a rising-portfolio backtest would be silently understated. Pin a
/// 100 → 150 → 75 trajectory so the rising-peak update is exercised in
/// isolation: the expected 50% drawdown is computable only against the new
/// peak of 150, not the initial 100 (which would give 25%).
/// </summary>
public class HoldingsBacktestCalculatorComputeMaxDrawdownRisingPeakTests
{
    [Fact]
    public void ComputeMaxDrawdown_ValueExceedsInitialThenDropsBelow_DrawdownMeasuredFromNewPeak()
    {
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "ComputeMaxDrawdown",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (decimal)method.Invoke(null, [new[] { 100m, 150m, 75m }]);

        result.Should().Be(50m);
    }
}
