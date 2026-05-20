using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsRenderComparisonTableEmptyWithSkippedTests
{
    [Fact]
    public void RenderComparisonTable_NoRowsButTickersSkipped_StillEmitsSkippedDiagnostic()
    {
        // RenderComparisonTable was extracted in #1480 as the markdown
        // renderer for CompareFinancialFact. The MCP CompareFinancialFact
        // tool routes through this helper for every multi-ticker comparison
        // request. The helper writes two independent trailing lines based
        // on input shape:
        //   if (rows.Count == 0) → "No company reported ..."
        //   if (skipped.Count > 0) → "Skipped: <tickers>"
        // The two conditions are INDEPENDENT — both can fire on the same
        // call.
        //
        // The risk this catches: a refactor that conjoins them into
        //   if (rows.Count > 0 && skipped.Count > 0) → "Skipped: ..."
        // (perhaps under the false intuition that "no rows means no
        // useful output, so suppress the skipped notice too") would
        // compile, pass any test that has at least one reporting company,
        // and silently hide diagnostic info from operators in exactly the
        // case where they need it most — when EVERY candidate ticker was
        // filtered out and the user sees a blank table with no
        // explanation. Investigating a "the tool returned nothing"
        // bug-report without the skipped list means re-running the query
        // by hand for every candidate.
        //
        // Pin: feed rows empty + skipped non-empty; assert the rendered
        // markdown contains the placeholder AND each skipped ticker so
        // both trailing lines are present.
        var method = typeof(FinancialFactsTools).GetMethod(
            "RenderComparisonTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var rows = new List<(string Ticker, string Name, FinancialFact Fact)>();
        var skipped = new List<string> { "AAPL", "MSFT" };

        var result = (string)
            method.Invoke(null, ["Revenues", 2024, SecFiscalPeriod.FullYear, rows, skipped]);

        result.Should().Contain("No company reported");
        result.Should().Contain("AAPL");
        result.Should().Contain("MSFT");
    }
}
