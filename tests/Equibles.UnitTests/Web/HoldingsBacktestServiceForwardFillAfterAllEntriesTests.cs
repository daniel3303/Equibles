using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// ForwardFill's contract: "binary search for the largest Date &lt;= the
/// requested date." When the requested date falls AFTER every entry in the
/// series, the correct answer is the LAST entry's price — the most recent
/// known close is forward-filled into the future gap. Three siblings pin the
/// exact-hit, between-entries, and before-all-entries paths; this one closes
/// the remaining boundary. A refactor that changed the loop condition from
/// <c>midDate &lt;= date</c> to <c>midDate &lt; date</c> would still pass
/// the between-entries test but would return null here (no entry strictly
/// precedes a date that equals the last entry — but more importantly, when
/// the date is past the last entry, the &lt; variant still matches, so only
/// subtle loop-exit bugs surface). Pinning the after-all boundary catches
/// any regression that truncates the search early.
/// </summary>
public class HoldingsBacktestServiceForwardFillAfterAllEntriesTests
{
    [Fact]
    public void ForwardFill_RequestedDateAfterAllEntries_ReturnsLastEntryPrice()
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
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 10), 100m]), 0);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 15), 110m]), 1);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 20), 120m]), 2);

        var forwardFill = typeof(HoldingsBacktestService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m =>
                m.Name == "ForwardFill"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == priceRowType.MakeArrayType()
            );

        // Request a date well past the last entry — forward fill must return
        // the most recent known price (120m from 2025-01-20).
        var result = (decimal?)forwardFill.Invoke(null, [series, new DateOnly(2025, 3, 1)]);

        result.Should().Be(120m);
    }
}
