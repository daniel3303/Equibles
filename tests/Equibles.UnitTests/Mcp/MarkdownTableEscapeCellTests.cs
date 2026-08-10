using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class MarkdownTableEscapeCellTests
{
    [Fact]
    public void EscapeCell_BackslashBeforePipe_KeepsPipeEscapedUnderCommonMarkRules()
    {
        var result = MarkdownTable.EscapeCell("A\\|B\r\nC");

        result.Should().Be("A\\\\\\|B  C");
    }

    [Fact]
    public void EscapeCell_Null_UsesRequestedEmptyValue()
    {
        MarkdownTable.EscapeCell(null, "—").Should().Be("—");
    }
}
