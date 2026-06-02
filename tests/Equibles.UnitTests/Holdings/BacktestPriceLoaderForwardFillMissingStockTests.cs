using System.Reflection;
using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

public class BacktestPriceLoaderForwardFillMissingStockTests
{
    // The dictionary overload is the load-bearing entry point the calculator actually calls
    // (priceOf: (stockId, date) => ForwardFill(pricesByStock, stockId, date)). pricesByStock is
    // built by GroupBy, so it only ever holds keys for stocks that had price rows in the window —
    // a held constituent with no loaded prices is simply absent. HoldingsBacktestCalculator's
    // contract is that priceOf returns null for such a stock so the position is excluded that day,
    // never that it throws. A refactor to the `[]` indexer would raise KeyNotFoundException and
    // abort the whole simulation. Pass a stockId absent from the map and assert null, not a throw.
    [Fact]
    public void ForwardFill_StockIdAbsentFromPriceMap_ReturnsNullWithoutThrowing()
    {
        var loaderType = typeof(FundScoringManager).Assembly.GetType(
            "Equibles.Holdings.BusinessLogic.BacktestPriceLoader"
        );
        var priceRowType = loaderType.GetNestedType("PriceRow");
        var mapType = typeof(Dictionary<,>).MakeGenericType(
            typeof(Guid),
            priceRowType.MakeArrayType()
        );
        var emptyMap = Activator.CreateInstance(mapType);

        var forwardFill = loaderType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                m.Name == "ForwardFill"
                && m.GetParameters().Length == 3
                && m.GetParameters()[0].ParameterType == mapType
            );

        var result = (decimal?)
            forwardFill.Invoke(null, [emptyMap, Guid.NewGuid(), new DateOnly(2025, 1, 20)]);

        result.Should().BeNull();
    }
}
