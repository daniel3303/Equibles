using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsResolveStockCellsTests
{
    // ResolveStockCells is the single chokepoint that converts an
    // unresolved-stock GUID into the (Ticker, Name) cell pair every
    // holdings-table renderer emits — RenderTopHoldersTable,
    // RenderInstitutionPortfolio, RenderBuyersSellersTable,
    // RenderMostHeldStocksTable, RenderQuarterlyActivity. Its contract
    // is the documented fallback:
    //   • Ticker on unresolved stock → "—" (em-dash, U+2014)
    //   • Name on unresolved stock   → "Unknown"
    // The two fallback strings are DISTINCT and SEMANTICALLY ASYMMETRIC:
    //   • "—" signals "no public symbol to display" — short, table-friendly.
    //   • "Unknown" signals "no name lookup succeeded" — longer, sentence-ish.
    // Swapping them is the single most likely refactor regression —
    // both literals appear together at one source line and "consolidate
    // the placeholder" is a tempting cleanup pass.
    //
    // The risk this pin uniquely catches: ResolveStockCells is private static
    // and currently untested. Holdings tables typically run against a
    // resolved CUSIP→CommonStock mapping, but the unresolved path fires
    // on every legitimate holding whose stock fell out of the master
    // CommonStocks table mid-period (delistings, ticker changes that
    // re-keyed the row, FTD scraper backfill gaps). A regression that:
    //   • Swapped the fallbacks (`Ticker → "Unknown", Name → "—"`)
    //     would compile, every other test that doesn't assert specifically
    //     on the missing-stock case would pass, and the institution-
    //     portfolio MCP tool's output would render rows as
    //     "Unknown — —" instead of "— — Unknown" for every delisted
    //     security in a manager's history.
    //   • Collapsed both fallbacks to one literal would similarly compile,
    //     and the output would render one column missing.
    //
    // The unresolved case (passing a Guid that's NOT in the dictionary) is
    // the structurally distinct path the public renderers can't easily
    // exercise from a unit test — the renderer takes already-loaded stocks
    // and the resolution happens at the caller's DB level. Reflection-
    // invoking the private helper with an empty dictionary is the cleanest
    // way to drive only this branch.
    //
    // Pin: pass an EMPTY stocks dictionary and a random GUID; assert the
    // exact em-dash literal for ticker and the exact "Unknown" string for
    // name. A swap surfaces as "(Unknown, —)"; a consolidation surfaces
    // as "(X, X)" for some single X; either fails. The pair of asserted
    // literals also defends against an Encoding-mismatch regression
    // (e.g. someone replacing the em-dash with an ASCII hyphen "-").
    [Fact]
    public void ResolveStockCells_StockIdNotInDictionary_ReturnsEmDashTickerAndUnknownName()
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "ResolveStockCells",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stocks = new Dictionary<Guid, CommonStock>();
        var missingId = Guid.NewGuid();

        var result = ((string Ticker, string Name))method!.Invoke(null, [stocks, missingId]);

        result.Ticker.Should().Be("—");
        result.Name.Should().Be("Unknown");
    }
}
