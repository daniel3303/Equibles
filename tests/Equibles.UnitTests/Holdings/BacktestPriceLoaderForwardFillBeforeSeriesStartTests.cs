using System.Reflection;
using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

public class BacktestPriceLoaderForwardFillBeforeSeriesStartTests
{
    // ForwardFill's inline contract: "largest close on or before `date` ... null when the series
    // starts later." This is the look-ahead-safety boundary of the backtest — a simulation day
    // that predates every priced entry has no honest close yet, so the position must be excluded
    // (null), never marked to the FIRST (future) close. An off-by-one (matchIdx seeded at 0) or a
    // flipped comparison would leak series[0]'s future price and silently corrupt mark-to-market.
    // Pick a date strictly before the earliest entry so leaking the first close is distinguishable.
    [Fact]
    public void ForwardFill_DateBeforeFirstEntry_ReturnsNullWithoutLeakingFirstClose()
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

        var result = (decimal?)forwardFill.Invoke(null, [series, new DateOnly(2024, 12, 31)]);

        result.Should().BeNull();
    }
}
