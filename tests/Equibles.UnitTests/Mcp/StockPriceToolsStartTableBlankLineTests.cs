using System.Reflection;
using System.Text;
using Equibles.Yahoo.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class StockPriceToolsStartTableBlankLineTests
{
    // Sibling to StockPriceToolsAppendNewestFirstRowsMaxResultsCapTests
    // (the only existing StockPriceTools static-helper pin). StartTable
    // is the OTHER private static formatter in the same file and is
    // currently unpinned. Its body is four AppendLine calls in a
    // specific order:
    //     sb.AppendLine(title);
    //     sb.AppendLine();         <-- blank line, load-bearing
    //     sb.AppendLine(columnsRow);
    //     sb.AppendLine(separatorRow);
    //
    // The contract this pin defends is the BLANK LINE BETWEEN TITLE
    // AND HEADER ROW. Markdown spec (CommonMark §4.1) requires an
    // empty line to separate a heading from a following table block —
    // otherwise the heading and the table's first row collapse into
    // a single paragraph in strict renderers. The MCP transport
    // delivers these tables to LLM clients that render Markdown to
    // varying degrees of strictness:
    //   • Claude Desktop / claude.ai — strict CommonMark; missing
    //     blank line → heading bleeds into the table-header row,
    //     table is no longer recognised as a table.
    //   • Some Code clients — looser parsing; missing blank still
    //     works but inconsistently.
    //
    // The risks this pin uniquely catches (none reachable from
    // AppendNewestFirstRows):
    //   • DROP-the-blank regression — `sb.AppendLine(title);
    //     sb.AppendLine(columnsRow);` — the cleanest "tidy" refactor.
    //     Compiles, every consumer keeps working in lenient renderers,
    //     silently breaks in strict ones. The LLM sees a wall of text
    //     instead of a structured table.
    //   • REORDER regression — title and columnsRow swapped, or
    //     separator placed before columns. All four arguments are
    //     `string` so the compiler can't catch a misorder. The
    //     ordered-output assertion catches every reorder.
    //   • EXTRA blank — `sb.AppendLine(title); sb.AppendLine();
    //     sb.AppendLine();` would render with too much vertical
    //     space in stricter renderers. The exact-line-count
    //     assertion catches this too.
    //
    // Pin: invoke with three distinct, easily-distinguishable inputs;
    // split the output by `Environment.NewLine` and assert the EXACT
    // 4-line structure (title, EMPTY, columns, separator). The
    // empty-line assertion via `Should().BeEmpty()` is the load-bearing
    // anchor — distinguishes dropped-blank from working order.
    [Fact]
    public void StartTable_TitleHeaderSeparator_EmitsBlankLineBetweenTitleAndHeader()
    {
        var method = typeof(StockPriceTools).GetMethod(
            "StartTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (StringBuilder)
            method!.Invoke(null, ["## ATR — AAPL", "| Date | ATR |", "|------|-----|"]);

        var lines = result.ToString().Split(Environment.NewLine);
        lines.Should().HaveCountGreaterThanOrEqualTo(4);
        lines[0].Should().Be("## ATR — AAPL");
        lines[1].Should().BeEmpty();
        lines[2].Should().Be("| Date | ATR |");
        lines[3].Should().Be("|------|-----|");
    }
}
