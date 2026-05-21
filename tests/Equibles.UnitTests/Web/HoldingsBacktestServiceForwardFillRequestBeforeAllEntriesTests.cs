using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceForwardFillRequestBeforeAllEntriesTests
{
    // ForwardFill's inline contract is "binary search for the largest Date <=
    // the requested date" — when the requested date is strictly before every
    // entry's date, there is no last-trading-day to fall back to, and the
    // helper must return null. The mark-to-market loop relies on that null to
    // skip the day's contribution rather than fabricate a price from the
    // earliest future entry (which would inject lookahead bias into the
    // simulated portfolio value). The sibling DateStrictlyBetweenTwoEntries
    // pin covers the between-entries arm; this pin covers the before-all-
    // entries arm — a refactor that flipped the matchIdx initialisation or
    // changed `<=` to `>=` would silently return the FIRST entry's price for
    // a pre-window date.
    [Fact]
    public void ForwardFill_RequestedDateStrictlyBeforeEveryEntry_ReturnsNull()
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
        var series = Array.CreateInstance(priceRowType, 2);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 15), 100m]), 0);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 2, 1), 200m]), 1);

        var forwardFill = typeof(HoldingsBacktestService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m =>
                m.Name == "ForwardFill"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == priceRowType.MakeArrayType()
            );

        // 2025-01-10 strictly precedes the earliest entry (2025-01-15); no
        // "last trading day on or before" exists.
        var result = (decimal?)forwardFill.Invoke(null, [series, new DateOnly(2025, 1, 10)]);

        result.Should().BeNull();
    }
}
