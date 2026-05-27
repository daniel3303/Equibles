using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Sibling to <see cref="HoldingsBacktestCalculatorComputeSummaryZeroInitialTests"/>.
/// That pin protects the <c>initial &lt;= 0</c> arm; this one protects the
/// earlier-still <c>values.Count == 0</c> arm. Both guards are load-bearing
/// because the next two lines — <c>values[0]</c> and <c>values[^1]</c> —
/// throw <see cref="ArgumentOutOfRangeException"/> on an empty list. A
/// refactor that "tidied" the empty-list guard ("the series always has at
/// least one entry from the rebalance loop") would compile cleanly, pass the
/// zero-initial pin, and crash the backtest the first time a quarter's
/// MarkToMarket produced no points — a real outcome when every snapshot's
/// price-fetch returns null in the simulated window.
/// </summary>
public class HoldingsBacktestCalculatorComputeSummaryEmptySeriesTests
{
    [Fact]
    public void ComputeSummary_EmptySeries_ReturnsDefaultSummaryWithoutThrowing()
    {
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "ComputeSummary",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        BacktestStrategySummary result = null;
        var act = () =>
            result = (BacktestStrategySummary)method.Invoke(null, [Enumerable.Empty<decimal>()]);

        act.Should().NotThrow();
        result.TotalReturnPercent.Should().Be(0m);
        result.CagrPercent.Should().Be(0m);
        result.MaxDrawdownPercent.Should().Be(0m);
    }
}
