using System.Reflection;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class HoldingsBacktestServiceForwardFillDictionaryMissingStockTests
{
    // The sibling pins (Empty / Exact / Between / After / RequestBefore) all
    // exercise the *array* overload of ForwardFill — the inner helper.
    // Production code never calls the array overload directly; the per-day
    // simulation step uses the *dictionary* overload at HoldingsBacktestService.cs:234
    // which dispatches via TryGetValue.
    //
    // Contract: lookup the stock's price series and forward-fill to the
    // requested date; if no series exists for that stock (e.g. a holdings
    // row references a stock whose DailyStockPrice rows haven't been
    // backfilled yet, or whose CommonStock id is stale after a ticker
    // re-key), return null without throwing.
    //
    // The risk this pin uniquely catches and the array-overload siblings
    // cannot:
    //   • Drop the TryGetValue null arm — `pricesByStock[stockId]` instead
    //     of TryGetValue — `KeyNotFoundException` would abort the whole
    //     backtest on the first stale-id row instead of yielding null
    //     for that single day. Caller (the per-day simulation loop)
    //     treats null as "no marketable value today" and carries the
    //     previous quarter's value forward.
    //   • Substitute `.GetValueOrDefault(stockId)` — returns a default
    //     BacktestPriceRow[] which is **null** (record-struct array
    //     default), and the array overload would NRE on `series.Length`.
    //     This pin catches the substitution; the empty-series sibling
    //     does not — that one supplies an explicit Array.CreateInstance
    //     of length 0, not null.
    //
    // Strategy: pass an empty Dictionary<Guid, BacktestPriceRow[]> and a
    // random stockId; assert null without throwing. BacktestPriceRow is
    // a private nested record struct — reflection walks the type to
    // build the correctly-typed dictionary instance.
    [Fact]
    public void ForwardFill_StockIdMissingFromDictionary_ReturnsNullWithoutThrowing()
    {
        var priceRowType = typeof(HoldingsBacktestService).GetNestedType(
            "BacktestPriceRow",
            BindingFlags.NonPublic
        );
        var dictType = typeof(Dictionary<,>).MakeGenericType(
            typeof(Guid),
            priceRowType!.MakeArrayType()
        );
        var emptyDict = Activator.CreateInstance(dictType);
        var missingStockId = Guid.NewGuid();

        var forwardFill = typeof(HoldingsBacktestService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m =>
                m.Name == "ForwardFill"
                && m.GetParameters().Length == 3
                && m.GetParameters()[0].ParameterType == dictType
            );

        decimal? result = 1m;
        var act = () =>
            result = (decimal?)
                forwardFill.Invoke(null, [emptyDict, missingStockId, new DateOnly(2025, 6, 15)]);

        act.Should().NotThrow();
        result.Should().BeNull();
    }
}
