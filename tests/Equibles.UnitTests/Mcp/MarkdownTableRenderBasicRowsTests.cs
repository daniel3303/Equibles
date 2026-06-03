using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class MarkdownTableRenderBasicRowsTests
{
    [Fact]
    public void Render_NoSubtitleWithRows_EmitsTitleBlankHeaderThenRows()
    {
        // Contract: the no-subtitle Render builds title + load-bearing blank line + header +
        // separator, then one line per row. The empty short-circuit and the subtitle overload are
        // covered; this basic non-empty path (3-arg Start + row loop) is the remaining uncovered
        // branch — a regression skipping rows or the blank line would break the rendered table.
        var nl = Environment.NewLine;
        var result = MarkdownTable.Render(
            rows: new[] { "10-K" },
            emptyMessage: "No filings.",
            title: "## Filings",
            headerRow: "| Form |",
            separatorRow: "|---|",
            renderRow: f => $"| {f} |"
        );

        result.Should().Contain($"## Filings{nl}{nl}| Form |");
        result.Should().Contain("| 10-K |");
        result.Should().NotBe("No filings.");
    }
}
