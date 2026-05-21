using System.Collections;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsBuildComparisonRowsDistinguishedSkipTests
{
    // BuildComparisonRows (extracted in #1580) partitions the user-supplied
    // tickers into two distinct skip buckets: "(not found)" when the ticker
    // doesn't resolve to any stock, vs "(no data)" when the stock is known
    // but has no reported fact for the requested period. The two messages
    // mean different things to the LLM consumer — unknown ticker is a typo
    // / coverage gap, no-data is a real-data absence — and conflating them
    // (e.g. via a single conjoined TryGetValue chain) would silently hide
    // which problem the operator should investigate.
    [Fact]
    public void BuildComparisonRows_KnownStockWithNoFact_SkippedAsNoDataNotNotFound()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var requested = new List<string> { "AAPL" };
        var stockByTicker = new Dictionary<string, CommonStock> { ["AAPL"] = apple };
        var bestByStock = new Dictionary<Guid, FinancialFact>();

        var method = typeof(FinancialFactsTools).GetMethod(
            "BuildComparisonRows",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = method.Invoke(null, [requested, stockByTicker, bestByStock]);

        var skipped = (List<string>)result.GetType().GetField("Item2").GetValue(result);
        var rows = (IList)result.GetType().GetField("Item1").GetValue(result);

        rows.Count.Should().Be(0);
        skipped.Should().ContainSingle().Which.Should().Be("AAPL (no data)");
    }
}
