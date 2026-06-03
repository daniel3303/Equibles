using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class MarkdownTableRenderEmptyTests
{
    [Fact]
    public void Render_NoRows_ReturnsEmptyMessageVerbatimWithoutTableScaffolding()
    {
        // Contract (doc): Render "returns emptyMessage verbatim when there are none". An empty
        // result set must surface the caller's message exactly — not an empty table (title +
        // header + separator with zero rows), which would read as a malformed/blank table to MCP
        // consumers. Only integration tests exercise Render; the empty short-circuit is unit-unpinned.
        var result = MarkdownTable.Render(
            rows: new List<int>(),
            emptyMessage: "No results found.",
            title: "## Results",
            headerRow: "| Value |",
            separatorRow: "|---|",
            renderRow: x => $"| {x} |"
        );

        result.Should().Be("No results found.");
    }
}
