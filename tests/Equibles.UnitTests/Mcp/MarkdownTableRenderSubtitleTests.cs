using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class MarkdownTableRenderSubtitleTests
{
    [Fact]
    public void Render_WithSubtitleAndRows_KeepsLoadBearingBlankLineBeforeHeader()
    {
        // Contract (doc): the subtitle Render emits title, subtitle, then a load-bearing BLANK
        // line before the header — strict CommonMark renderers need it to recognise the following
        // rows as a table. The subtitle overload is unit-untested; a regression dropping the blank
        // line would render the table as plain text in MCP clients.
        var nl = Environment.NewLine;
        var result = MarkdownTable.Render(
            rows: new[] { 1 },
            emptyMessage: "No results.",
            title: "## Results",
            subtitle: "Showing 1 of 1",
            headerRow: "| Value |",
            separatorRow: "|---|",
            renderRow: x => $"| {x} |"
        );

        result.Should().Contain($"Showing 1 of 1{nl}{nl}| Value |");
        result.Should().Contain($"| 1 |");
    }
}
