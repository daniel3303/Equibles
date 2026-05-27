using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsRenderFactHistoryTableLatestRestatedLabelTests
{
    // Sibling to RenderFactHistoryTableOriginallyReportedLabelTests
    // which pins the `asOriginallyReported = true` arm. This pin
    // defends the OPPOSITE arm of the ternary:
    //   asOriginallyReported ? "as originally reported" : "latest restated"
    //
    // The default for `asOriginallyReported` in the calling tool is
    // false — so "latest restated" is the arm every MCP query takes
    // unless the user explicitly opts in to original-filed numbers.
    // It's the LOAD-BEARING arm in production volume; the existing
    // sibling pin defends the rarer opt-in path.
    //
    // The risk this pin uniquely catches and the existing sibling
    // cannot:
    //   • Ternary collapse to a constant — `var basis = "as
    //     originally reported";` (a maintainer "simplifying"
    //     after seeing the existing sibling pin pass) — would
    //     compile, pass the existing pin (its expected substring
    //     still appears), and silently relabel every default
    //     MCP response. Users querying "latest Apple revenue"
    //     would see the heading "as originally reported" suggesting
    //     they're looking at the 10-K's first-filed numbers when
    //     they're actually seeing the most-recent restated values.
    //
    //   • Swap of the two labels — `asOriginallyReported ? "latest
    //     restated" : "as originally reported"` — would FAIL the
    //     existing sibling pin (which asserts NotContain "latest
    //     restated") so it'd be caught — but only one side of the
    //     pair. This pin makes the symmetry explicit.
    //
    //   • Label text drift — "latest" → "latest" (rename to "newest"
    //     under "consistency" with other UI labels) — would compile
    //     and pass the existing pin. The MCP-rendered table header
    //     would no longer match the established financial-reporting
    //     vocabulary that analysts and the LLM are trained on.
    //
    // Pin: invoke RenderFactHistoryTable with asOriginallyReported
    // = false (the default), assert "latest restated" appears AND
    // "as originally reported" does NOT. Mirrors the sibling's
    // shape exactly for review symmetry.
    [Fact]
    public void RenderFactHistoryTable_AsOriginallyReportedFalse_HeadingSaysLatestRestatedNotOriginallyReported()
    {
        var method = typeof(FinancialFactsTools).GetMethod(
            "RenderFactHistoryTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var perPeriod = new List<FinancialFact>();

        var result = (string)method.Invoke(null, ["Revenues", stock, false, perPeriod]);

        result.Should().Contain("latest restated");
        result.Should().NotContain("as originally reported");
    }
}
