using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsRenderFactHistoryTableOriginallyReportedLabelTests
{
    [Fact]
    public void RenderFactHistoryTable_AsOriginallyReportedTrue_HeadingSaysOriginallyReportedNotLatestRestated()
    {
        // RenderFactHistoryTable (extracted in #1485) renders the heading
        // basis label from a single ternary:
        //   asOriginallyReported ? "as originally reported" : "latest restated"
        // The two labels are semantically OPPOSITE — "as originally reported"
        // returns the values from the first 10-K/10-Q filing for the period,
        // while "latest restated" returns the values from the most recent
        // amendment (10-K/A, 10-Q/A). Financial analysts rely on the label
        // to know which lens the data was viewed through; the LLM exposes
        // it verbatim to the user.
        //
        // The risk this catches: a refactor that swaps the ternary arms —
        // either by accident during an "is this expression more readable
        // flipped?" cleanup, or because someone misread the boolean
        // direction — would compile, pass any test that doesn't probe the
        // exact label text, and silently mislabel every history rendering.
        // A user analyzing "as originally reported" Apple revenue would
        // actually be looking at restated numbers, and vice versa. The
        // labels are user-visible in MCP responses and feed directly
        // into LLM-generated narrative.
        //
        // Pin the asOriginallyReported = true arm: the rendered string
        // must contain "as originally reported" and must NOT contain
        // "latest restated".
        var method = typeof(FinancialFactsTools).GetMethod(
            "RenderFactHistoryTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var perPeriod = new List<FinancialFact>();

        var result = (string)method.Invoke(null, ["Revenues", stock, true, perPeriod]);

        result.Should().Contain("as originally reported");
        result.Should().NotContain("latest restated");
    }
}
