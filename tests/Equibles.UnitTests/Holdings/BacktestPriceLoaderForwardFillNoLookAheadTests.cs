using System.Reflection;
using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

public class BacktestPriceLoaderForwardFillNoLookAheadTests
{
    // BacktestPriceLoader exists to run a "look-ahead-safe" backtest: its inline contract is
    // "largest [Date] on or before the requested date" so a simulation day landing on a weekend
    // or holiday resolves to the LAST trading day's close, never a future one. This implementation
    // is a separate copy from the Web-layer HoldingsBacktestService twin and is otherwise untested.
    // A flipped comparison (midDate < date instead of <=, or matchIdx/lo updates swapped) would
    // leak the NEXT entry's price and silently corrupt mark-to-market. Pick a date strictly between
    // two entries so returning the future entry's price is distinguishably wrong.
    [Fact]
    public void ForwardFill_DateStrictlyBetweenTwoEntries_ReturnsLatestEntryOnOrBeforeDate()
    {
        var loaderType = typeof(FundScoringManager).Assembly.GetType(
            "Equibles.Holdings.BusinessLogic.BacktestPriceLoader"
        );
        var priceRowType = loaderType.GetNestedType("PriceRow");
        var priceRowCtor = priceRowType.GetConstructor([
            typeof(Guid),
            typeof(DateOnly),
            typeof(decimal),
        ]);

        var stockId = Guid.NewGuid();
        var series = Array.CreateInstance(priceRowType, 3);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 1), 100m]), 0);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 15), 200m]), 1);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 2, 1), 300m]), 2);

        var forwardFill = loaderType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                m.Name == "ForwardFill"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == priceRowType.MakeArrayType()
            );

        var result = (decimal?)forwardFill.Invoke(null, [series, new DateOnly(2025, 1, 20)]);

        result.Should().Be(200m);
    }
}
