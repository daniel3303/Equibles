using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerResolveStockCellsTests
{
    // Asymmetry pin against the HoldingsActivityController.ResolveStockCells
    // sibling. Two ResolveStockCells helpers coexist in the Web project:
    //   • HoldingsActivityController — fallback ("—", "Unknown") for the
    //     interactive dashboard, where display-friendly placeholders read
    //     better in HTML tables.
    //   • HoldingsExportController (this one) — fallback ("", "") for the
    //     CSV export, where an empty cell is the correct neighbouring-tool
    //     contract (Excel, pandas, etc. read "—" as a literal string and
    //     "Unknown" as a real value, both of which corrupt downstream
    //     aggregation).
    //
    // Contract (HoldingsExportController.cs:308):
    //   stocks.TryGetValue(stockId, out var stock);
    //   return (stock?.Ticker ?? string.Empty, stock?.Name ?? string.Empty);
    //
    // The risk this pin uniquely catches and the Activity sibling cannot:
    //   • Cross-helper harmonisation. A maintainer applying the
    //     Activity sibling's fallback to "stay consistent" would compile,
    //     pass the Activity pin, and silently break every CSV export
    //     ingested by an external tool — "—" / "Unknown" cells appear
    //     in pandas/Excel as real values, corrupting aggregations.
    //   • Swap regression — `Ticker ?? "Unknown", Name ?? "—"` (or any
    //     non-empty fallback) flipped in. Caught by the exact-string
    //     assertion on both columns.
    //
    // The unresolved-stock branch fires on every CSV export row whose
    // stock fell out of the master CommonStocks table mid-period
    // (delistings, ticker re-keys, FTD scraper backfill gaps).
    //
    // Strategy: pass an empty StockLabel dictionary and a random GUID;
    // assert empty-string literals for both ticker and name. StockLabel
    // is a private nested record — reflection walks the type to build
    // the correctly-typed Dictionary<Guid, StockLabel> argument.
    [Fact]
    public void ResolveStockCells_StockIdNotInDictionary_ReturnsEmptyTickerAndEmptyName()
    {
        var controllerType = typeof(HoldingsExportController);
        var stockLabelType = controllerType.GetNestedType("StockLabel", BindingFlags.NonPublic);
        var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(Guid), stockLabelType!);
        var stocks = Activator.CreateInstance(dictType);
        var missingId = Guid.NewGuid();

        var method = controllerType.GetMethod(
            "ResolveStockCells",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = ((string Ticker, string Name))method!.Invoke(null, [stocks, missingId])!;

        result.Ticker.Should().Be(string.Empty);
        result.Name.Should().Be(string.Empty);
    }
}
