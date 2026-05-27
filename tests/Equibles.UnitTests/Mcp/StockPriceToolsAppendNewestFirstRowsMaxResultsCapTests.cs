using System.Reflection;
using System.Text;
using Equibles.Yahoo.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class StockPriceToolsAppendNewestFirstRowsMaxResultsCapTests
{
    [Fact]
    public void AppendNewestFirstRows_CountExceedsMaxResults_EmitsExactlyMaxRowsNewestFirst()
    {
        // AppendNewestFirstRows (StockPriceTools.cs:389-402) iterates
        // `for (var i = count - 1; i >= 0 && emitted < maxResults; i--)` —
        // the maxResults cap exists so the MCP response doesn't blow up
        // when a price-series query returns thousands of rows but the tool
        // contract advertises a small page size. The newest-first ordering
        // matters because indicator tables (ATR, OBV, Stochastic) are
        // chronologically backwards-loaded; the LLM expects the most-recent
        // row at the top. A refactor that drops the `&& emitted < maxResults`
        // sub-condition (perhaps "the caller already limits with .Take()")
        // would emit every row. A swap to a forward iteration would emit
        // the OLDEST maxResults rows. Pin both: count=5, maxResults=2 →
        // exactly two rows, and they must be the indices 4 and 3 (in that
        // order, newest first).
        var method = typeof(StockPriceTools).GetMethod(
            "AppendNewestFirstRows",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = new StringBuilder();
        Func<int, string> formatRow = i => $"row{i}";

        method!.Invoke(null, [result, 5, 2, formatRow]);

        var lines = result
            .ToString()
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().Be("row4");
        lines[1].Should().Be("row3");
    }
}
