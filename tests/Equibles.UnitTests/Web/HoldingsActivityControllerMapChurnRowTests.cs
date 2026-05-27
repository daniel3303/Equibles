using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Holdings;

namespace Equibles.UnitTests.Web;

public class HoldingsActivityControllerMapChurnRowTests
{
    // MapChurnRow is the projector that turns a MarketWideStockChurn repo row
    // into a HoldingsActivityRow view-model for the "stocks gaining/losing
    // filers" widget on the activity dashboard. It does THREE distinct
    // mappings:
    //   • CommonStockId (direct copy)
    //   • NewFilerCount (direct copy — "filers who newly took a position")
    //   • SoldOutFilerCount (direct copy — "filers who exited their position")
    //   Plus ResolveStockCells for ticker/name.
    //
    // The risk this pin uniquely catches: MapChurnRow is private static
    // and currently untested. NewFilerCount and SoldOutFilerCount are
    // SEMANTICALLY OPPOSITE — one counts entries, the other counts exits.
    // A SWAP regression (`NewFilerCount = churn.SoldOutFilerCount`,
    // `SoldOutFilerCount = churn.NewFilerCount`) under a careless
    // copy-paste during a rename would compile cleanly, pass every other
    // test (none exercise this projector), and silently INVERT the
    // "most-bought" and "most-sold" rankings across the entire
    // institutional-activity dashboard. The widget renders a "Gainers"
    // and "Losers" pane side by side; swapped fields put the same
    // stocks under both headings with inverted counts.
    //
    // The complementary regression: DROP-one-field — `SoldOutFilerCount`
    // never assigned — would default to 0 in HoldingsActivityRow,
    // showing zero exits on every stock and breaking the "stocks
    // losing filers" panel entirely.
    //
    // Pin: build a churn with DISTINCT, EASILY-DISTINGUISHABLE numeric
    // values (NewFilerCount=10, SoldOutFilerCount=20) so a swap surfaces
    // by which count lands in which output field. Assert each separately
    // — both `NewFilerCount == 10` AND `SoldOutFilerCount == 20`. Either
    // arm of the assertion failing pinpoints which field was misrouted.
    //
    // Reflection-invoke since MapChurnRow is private static. Use an
    // empty stocks dictionary — that route through ResolveStockCells'
    // unresolved-stock fallback is itself pinned (PR #2293).
    [Fact]
    public void MapChurnRow_DistinctCounts_RoutesNewAndSoldOutToCorrespondingFields()
    {
        var controllerType = typeof(HoldingsActivityController);
        var stockLabelType = controllerType.GetNestedType("StockLabel", BindingFlags.NonPublic);
        var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(Guid), stockLabelType!);
        var stocks = Activator.CreateInstance(dictType);

        var method = controllerType.GetMethod(
            "MapChurnRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stockId = Guid.NewGuid();
        var churn = new MarketWideStockChurn
        {
            CommonStockId = stockId,
            NewFilerCount = 10,
            SoldOutFilerCount = 20,
        };

        var row = (HoldingsActivityRow)method!.Invoke(null, [churn, stocks]);

        row.CommonStockId.Should().Be(stockId);
        row.NewFilerCount.Should().Be(10);
        row.SoldOutFilerCount.Should().Be(20);
    }
}
