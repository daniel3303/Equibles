using System.Reflection;
using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

public class BacktestPriceLoaderForwardFillAfterSeriesEndTests
{
    // ForwardFill's inline contract: "largest close on or before `date`". Forward-fill exists to
    // carry the last known close FORWARD, so a simulation day that lands after every priced entry
    // (a long market closure, a delisted/halted name, or simply a `to` date past the last trade)
    // must resolve to the LAST entry's close — never null. Returning null past the series end is
    // the silent failure mode of an exact-match lookup masquerading as forward-fill: positions
    // would drop out of the basket the moment fresh prices stop arriving, corrupting mark-to-market
    // exactly when it matters. This boundary (matchIdx settling on the last index, lo running off
    // the top) is unpinned — the existing tests cover only the between-days and before-start cases.
    // Pick a date strictly after the final entry so returning null instead of the last close fails.
    [Fact]
    public void ForwardFill_DateAfterLastEntry_ReturnsLastCloseNotNull()
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

        var result = (decimal?)forwardFill.Invoke(null, [series, new DateOnly(2025, 3, 1)]);

        result.Should().Be(300m);
    }
}
