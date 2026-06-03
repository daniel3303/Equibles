using System.Text;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class MarkdownTableAppendNumberedRowsTests
{
    // Contract: AppendNumberedRows passes a 1-BASED rank to renderRow, one row per item, in
    // order. The helper exists precisely to centralise the `i + 1` so call sites can't drift
    // to the 0-based `i`. A regression to 0-based would compile and silently renumber every
    // ranked MCP table (top movers, search results) starting at 0. Derive the oracle from the
    // doc: the first item is rank 1, not 0.
    [Fact]
    public void AppendNumberedRows_ThreeRows_PassesOneBasedRankInOrder()
    {
        var sb = new StringBuilder();

        sb.AppendNumberedRows(new[] { "a", "b", "c" }, (rank, item) => $"{rank}:{item}");

        var lines = sb.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Equal("1:a", "2:b", "3:c");
    }
}
