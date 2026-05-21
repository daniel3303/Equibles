using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceForwardFillBetweenEntriesTests
{
    // ForwardFill's inline contract is "binary search for the largest Date <= the requested
    // date" — used by Execute() so a simulation day that falls on a weekend or holiday
    // resolves to the LAST trading day's close, not the NEXT one. A flipped comparison
    // (e.g. midDate < date instead of <=, or matchIdx/lo updates swapped) would silently
    // return the wrong side: either null or the FUTURE price, both invalidating mark-to-
    // market. Pick a date strictly between two entries so the LAST-trading-day invariant
    // is observable — returning the next entry's price would distinguishably fail.
    [Fact]
    public void ForwardFill_DateStrictlyBetweenTwoEntries_ReturnsPriceOfLatestEntryOnOrBeforeDate()
    {
        var stockId = Guid.NewGuid();
        var priceRowType = typeof(HoldingsBacktestService).GetNestedType(
            "BacktestPriceRow",
            BindingFlags.NonPublic
        );
        var priceRowCtor = priceRowType.GetConstructor([
            typeof(Guid),
            typeof(DateOnly),
            typeof(decimal),
        ]);
        var series = Array.CreateInstance(priceRowType, 3);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 1), 100m]), 0);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 15), 200m]), 1);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 2, 1), 300m]), 2);

        var forwardFill = typeof(HoldingsBacktestService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m =>
                m.Name == "ForwardFill"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == priceRowType.MakeArrayType()
            );

        var result = (decimal?)forwardFill.Invoke(null, [series, new DateOnly(2025, 1, 20)]);

        result.Should().Be(200m);
    }
}
