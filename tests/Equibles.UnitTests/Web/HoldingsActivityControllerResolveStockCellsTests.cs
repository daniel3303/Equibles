using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsActivityControllerResolveStockCellsTests
{
    // Web-side sibling to InstitutionalHoldingsToolsResolveStockCellsTests
    // (which pins the same fallback contract on the MCP-side helper of the
    // same name). Two distinct `ResolveStockCells` helpers exist —
    // InstitutionalHoldingsTools (Holdings.Mcp) and HoldingsActivityController
    // (Web) — each owning its own implementation with the same fallback
    // strings ("—" for ticker, "Unknown" for name).
    //
    // Because the two helpers are parallel-evolved (same fallback contract,
    // different stock-type signatures: CommonStock vs StockLabel), a
    // cross-helper drift is the natural refactor risk — someone refactoring
    // the Mcp helper to drop the em-dash or shrink the literal would
    // typically leave this Web sibling unchanged, and vice-versa. This pin
    // closes the visible gap on the Web side.
    //
    // Contract:
    //   stocks.TryGetValue(stockId, out var stock);
    //   return (stock?.Ticker ?? "—", stock?.Name ?? "Unknown");
    //
    // The risk this pin uniquely catches and the Mcp sibling cannot:
    //   • Drift between the two ResolveStockCells helpers. The Mcp pin
    //     defends its helper's fallback; this pin defends the Web
    //     helper's identical fallback. A refactor that updated one but
    //     not the other (or updated both differently — e.g. someone
    //     changed the Web one to use "TBD" because the holdings-activity
    //     view "looked better with that text") would compile, pass
    //     the Mcp sibling, and silently render mis-matched fallbacks
    //     in the holdings-activity tables.
    //   • Swap regression on the Web side — `Ticker ?? "Unknown",
    //     Name ?? "—"` flips the two fallbacks. Catches the same
    //     class of asymmetric-fallback typo as the Mcp pin, locally.
    //   • Encoding regression — em-dash U+2014 → ASCII "-" — silently
    //     changes the visual width of every unresolved-stock row in
    //     the holdings-activity tables.
    //
    // The unresolved-stock branch fires on every legitimate holding
    // whose stock fell out of the master CommonStocks table mid-period
    // (delistings, ticker re-keys, FTD scraper backfill gaps); the
    // Web holdings-activity dashboard surfaces those rows in its
    // top-churn / new-positions / closed-positions widgets, so the
    // fallback is on the production hot path.
    //
    // Strategy: pass an empty StockLabel dictionary and a random GUID;
    // assert exact em-dash literal for ticker and "Unknown" for name.
    // StockLabel is a private nested class — reflection-invoke also
    // supplies the dictionary as the typed parameter via the
    // GetNestedType walk used elsewhere in this file's sibling tests.
    [Fact]
    public void ResolveStockCells_StockIdNotInDictionary_ReturnsEmDashTickerAndUnknownName()
    {
        var controllerType = typeof(HoldingsActivityController);
        var stockLabelType = controllerType.GetNestedType("StockLabel", BindingFlags.NonPublic);
        var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(Guid), stockLabelType!);
        var stocks = Activator.CreateInstance(dictType);
        var missingId = Guid.NewGuid();

        var method = controllerType.GetMethod(
            "ResolveStockCells",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = ((string Ticker, string Name))method!.Invoke(null, [stocks, missingId])!;

        result.Ticker.Should().Be("—");
        result.Name.Should().Be("Unknown");
    }
}
