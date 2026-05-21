using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceForwardFillExactEntryDateTests
{
    // ForwardFill's inline contract reads: "binary search for the largest Date
    // <= the requested date". Equality matters: when the simulation day lands
    // EXACTLY on a trading day, that day's close is the correct mark-to-market
    // price — not the prior day's. The two existing siblings
    // (DateStrictlyBetweenTwoEntries, RequestedDateStrictlyBeforeEveryEntry)
    // both miss a refactor that flipped `midDate <= date` to `midDate < date`:
    //   - between-entries: request 01-20 lands between 01-15 and 02-01 — both
    //     `<=` and `<` match 01-15, the test still passes.
    //   - before-all-entries: request 01-10 precedes every entry — both `<=`
    //     and `<` reject every entry, the test still passes.
    // Only an exact-boundary request distinguishes the two operators: with
    // `<=`, the matching entry is returned; with `<`, no entry satisfies the
    // predicate and the helper falls through to null. A regression that
    // "optimized" away the equality on the (false) assumption that "exact
    // hits are weekends" would silently null-out every on-trading-day price
    // and inject lookahead bias into mark-to-market.
    [Fact]
    public void ForwardFill_RequestedDateEqualsAnEntryDate_ReturnsThatEntrysPrice()
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

        var result = (decimal?)forwardFill.Invoke(null, [series, new DateOnly(2025, 1, 15)]);

        result.Should().Be(100m);
    }
}
