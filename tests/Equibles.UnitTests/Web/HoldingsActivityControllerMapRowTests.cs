using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Holdings;

namespace Equibles.UnitTests.Web;

public class HoldingsActivityControllerMapRowTests
{
    // Sibling to HoldingsActivityControllerMapChurnRowTests (PR #2312).
    // That pin defends the churn-projector's New/SoldOut field routing.
    // This pin covers the structurally distinct ACTIVITY-projector MapRow
    // which projects MarketWideStockActivity → HoldingsActivityRow for
    // the "stocks gaining/losing shares" heat map widget.
    //
    // MapRow does FOUR direct copies on top of ResolveStockCells:
    //   • DeltaShares (current − previous shares)
    //   • DeltaValue (current − previous $)
    //   • CurrentFilerCount (current count)
    //   • PreviousFilerCount (prior count)
    //
    // The risk this pin uniquely catches: MapRow is private static and
    // currently untested. Four PAIRED-VALUE fields means MULTIPLE swap
    // regressions are possible — each catastrophic on the dashboard:
    //   • CurrentFilerCount ↔ PreviousFilerCount swap — the activity
    //     dashboard renders a delta indicator (current − previous);
    //     swapping the two fields INVERTS the indicator on every row
    //     (stocks GAINING filers would show as LOSING filers, vice
    //     versa). The MapChurnRow sibling can't see this — different
    //     fields entirely.
    //   • DeltaShares ↔ DeltaValue swap — share counts and dollar
    //     values are wildly different magnitudes, so the dashboard's
    //     value-formatter (which formats DeltaValue as currency, e.g.
    //     "$1.2B") would render share counts as dollars and vice-versa.
    //     The header label says "Δ Shares" so a swap visibly shows
    //     huge dollar-formatted share counts in the shares column.
    //   • Drop-one-field — would default to 0 in HoldingsActivityRow,
    //     showing 0 across every row in the affected column.
    //
    // Pin: build a MarketWideStockActivity with DISTINCT values for
    // each pair so swaps are visible:
    //   CurrentShares=1000, PreviousShares=700 → DeltaShares=300
    //   CurrentValue=10000, PreviousValue=7000 → DeltaValue=3000
    //   CurrentFilerCount=50, PreviousFilerCount=40
    // Assert each output field separately so a swap fails on the
    // specific misrouted field.
    //
    // Reflection-invoke since MapRow is private static. Use an empty
    // stocks dictionary — ResolveStockCells' unresolved-stock fallback
    // is itself pinned (PR #2293).
    [Fact]
    public void MapRow_DistinctActivityFields_RoutesEachToCorrespondingViewModelProperty()
    {
        var controllerType = typeof(HoldingsActivityController);
        var stockLabelType = controllerType.GetNestedType("StockLabel", BindingFlags.NonPublic);
        var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(Guid), stockLabelType!);
        var stocks = Activator.CreateInstance(dictType);

        var method = controllerType.GetMethod(
            "MapRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stockId = Guid.NewGuid();
        var activity = new MarketWideStockActivity
        {
            CommonStockId = stockId,
            CurrentShares = 1000L,
            PreviousShares = 700L,
            CurrentValue = 10000L,
            PreviousValue = 7000L,
            CurrentFilerCount = 50,
            PreviousFilerCount = 40,
        };

        var row = (HoldingsActivityRow)method!.Invoke(null, [activity, stocks]);

        row.CommonStockId.Should().Be(stockId);
        row.DeltaShares.Should().Be(300L);
        row.DeltaValue.Should().Be(3000L);
        row.CurrentFilerCount.Should().Be(50);
        row.PreviousFilerCount.Should().Be(40);
    }
}
