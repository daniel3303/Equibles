using System.Reflection;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class HoldingsBacktestCalculatorComputeSummaryZeroInitialTests
{
    // ComputeSummary derives TotalReturn / CAGR / MaxDrawdown from a daily
    // portfolio-value series. The `if (initial <= 0) return new …` short-
    // circuit is load-bearing — `(final / initial - 1m) * 100m` does an
    // exact decimal division, and `decimal / 0m` throws DivideByZeroException
    // (unlike the IEEE-float NaN result that double would produce). The
    // initial value can legitimately be 0 in the backtest pipeline: a
    // zero-allocation Rebalance (currentValue=0) followed by a series of
    // MarkToMarket fallback-of-zero days yields a series like [0, 0, …, 0],
    // and even a partial wipeout can land the first MarkToMarket point at
    // exactly 0. A refactor that dropped the `<= 0` guard ("default(decimal)
    // can never be 0 in practice") would crash the simulation with an
    // unhandled exception on that edge.
    [Fact]
    public void ComputeSummary_SeriesStartingAtZero_ReturnsDefaultSummaryWithoutThrowing()
    {
        var method = typeof(HoldingsBacktestCalculator).GetMethod(
            "ComputeSummary",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        BacktestStrategySummary result = null;
        var act = () =>
            result = (BacktestStrategySummary)
                method.Invoke(null, [new[] { 0m, 50m, 100m }.AsEnumerable()]);

        act.Should().NotThrow();
        result.TotalReturnPercent.Should().Be(0m);
        result.CagrPercent.Should().Be(0m);
        result.MaxDrawdownPercent.Should().Be(0m);
    }
}
