using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceForwardFillEmptySeriesTests
{
    // ForwardFill is called per simulated day for every stock in the backtest
    // universe; a newly-listed name that has no historical prices yet hands in
    // an empty BacktestPriceRow[] from the upstream LoadPrices. The leading
    // `series.Length == 0 ? null` early-bail is the load-bearing safety —
    // without it the binary-search initialiser still works by accident
    // (`hi = -1` makes the loop never execute), but a refactor that started
    // pre-reading `series[0]` for any optimisation would IndexOutOfRange on
    // the first newly-listed stock and abort the entire simulation. Pin the
    // explicit empty-bail.
    [Fact]
    public void ForwardFill_EmptySeries_ReturnsNullWithoutThrowing()
    {
        var stockId = Guid.NewGuid();
        var priceRowType = typeof(HoldingsBacktestService).GetNestedType(
            "BacktestPriceRow",
            BindingFlags.NonPublic
        );
        var emptySeries = Array.CreateInstance(priceRowType, 0);

        var forwardFill = typeof(HoldingsBacktestService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m =>
                m.Name == "ForwardFill"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == priceRowType.MakeArrayType()
            );

        decimal? result = 1m;
        var act = () =>
            result = (decimal?)forwardFill.Invoke(null, [emptySeries, new DateOnly(2025, 6, 15)]);

        act.Should().NotThrow();
        result.Should().BeNull();
    }
}
