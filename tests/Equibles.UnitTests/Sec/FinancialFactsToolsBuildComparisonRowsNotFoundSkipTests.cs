using System.Collections;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsBuildComparisonRowsNotFoundSkipTests
{
    // Sibling to FinancialFactsToolsBuildComparisonRowsDistinguishedSkipTests
    // (which pins the "(no data)" skip path — known stock, missing fact).
    // This pin defends the OTHER skip arm: "(not found)" — when the
    // requested ticker doesn't resolve to any stock in stockByTicker.
    //
    // The body has TWO sequential early-continues:
    //     if (!stockByTicker.TryGetValue(ticker, out var stock))
    //     {
    //         skipped.Add($"{FactMarkdown.Cell(ticker)} (not found)");
    //         continue;
    //     }
    //     if (!bestByStock.TryGetValue(stock.Id, out var best))
    //     {
    //         skipped.Add($"{FactMarkdown.Cell(ticker)} (no data)");
    //         continue;
    //     }
    //
    // Each suffix means something different to the LLM consumer:
    //   • "(not found)" — the ticker isn't in the master CommonStocks
    //     table. Causes: typo, ADR symbol the user passed in lower
    //     case, delisted ticker, never-tracked exchange.
    //   • "(no data)" — the company exists but reported no fact for
    //     the requested concept in the requested period. Causes:
    //     concept not applicable to this issuer's filings, period
    //     pre-dates EDGAR coverage, taxonomy mismatch.
    //
    // The risks this pin uniquely catches and the existing sibling
    // cannot:
    //
    //   • Skip-suffix swap — `skipped.Add($"{ticker} (no data)")`
    //     in the FIRST early-continue (a copy-paste from below) —
    //     would compile, pass the existing "no data" sibling pin
    //     (its scenario hits the SECOND early-continue), and
    //     silently mislabel every unknown-ticker request as
    //     "no data". An operator chasing a typo would investigate
    //     coverage gaps instead.
    //
    //   • Skip-suffix drop — `skipped.Add($"{ticker}")` (just the
    //     ticker, suffix omitted) — would compile, pass the
    //     existing sibling pin (different path), and emit
    //     ambiguous skip lines the LLM can't disambiguate.
    //
    //   • Continue → fall-through regression — dropping the
    //     `continue;` after the FIRST skipped.Add would let the
    //     null `stock` flow into the second TryGetValue → NRE on
    //     `stock.Id`. The existing pin's stock IS non-null so
    //     this regression is invisible there.
    //
    // Adversarial input: a requested ticker with NO entry in
    // stockByTicker AND NO matching entry in bestByStock (the latter
    // could never match because there's no stock.Id to look up).
    // Assert (a) skipped contains exactly the "(not found)" message,
    // (b) it does NOT contain "(no data)" — separating the two
    // suffixes — and (c) rows is empty.
    [Fact]
    public void BuildComparisonRows_UnknownTicker_SkippedAsNotFoundNotNoData()
    {
        var requested = new List<string> { "XYZ" };
        var stockByTicker = new Dictionary<string, CommonStock>();
        var bestByStock = new Dictionary<Guid, FinancialFact>();

        var method = typeof(FinancialFactsTools).GetMethod(
            "BuildComparisonRows",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = method!.Invoke(null, [requested, stockByTicker, bestByStock]);

        var skipped = (List<string>)result.GetType().GetField("Item2").GetValue(result);
        var rows = (IList)result.GetType().GetField("Item1").GetValue(result);

        rows.Count.Should().Be(0);
        skipped.Should().ContainSingle().Which.Should().Be("XYZ (not found)");
        skipped[0].Should().NotContain("(no data)");
    }
}
