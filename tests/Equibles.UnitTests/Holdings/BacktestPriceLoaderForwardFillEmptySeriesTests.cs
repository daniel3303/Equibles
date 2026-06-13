using System.Reflection;
using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

public class BacktestPriceLoaderForwardFillEmptySeriesTests
{
    // ForwardFill is called per simulated day for every stock in the backtest universe; a
    // newly-listed name with no historical prices yet hands in an empty PriceRow[]. The leading
    // `series.Length == 0 ? null` early-bail is the load-bearing safety — the binary search works
    // by accident on an empty array (hi = -1 makes the loop never execute), but a refactor that
    // started pre-reading series[0] would IndexOutOfRange on the first newly-listed stock and abort
    // the entire simulation. Pin the explicit empty-bail.
    [Fact]
    public void ForwardFill_EmptySeries_ReturnsNullWithoutThrowing()
    {
        var loaderType = typeof(FundScoringManager).Assembly.GetType(
            "Equibles.Holdings.BusinessLogic.BacktestPriceLoader"
        );
        var priceRowType = loaderType.GetNestedType("PriceRow");
        var emptySeries = Array.CreateInstance(priceRowType, 0);

        var forwardFill = loaderType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
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
