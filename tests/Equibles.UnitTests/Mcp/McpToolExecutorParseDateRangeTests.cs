using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

public class McpToolExecutorParseDateRangeTests
{
    // ParseDateRange is the range gate every MCP tool composes from two
    // string args: Start = ParseDateOr(startText, defaultStart),
    // End = ParseDateOr(endText, today). This pins that valid ISO bounds
    // are returned verbatim and the supplied defaultStart fallback is NOT
    // substituted when startText is parseable — guarding the composition
    // against a future rewrite that drops the parse or swaps the fallback.
    [Fact]
    public void ParseDateRange_ValidIsoBounds_ReturnsParsedBoundsNotDefault()
    {
        var unusedDefaultStart = new DateOnly(1999, 12, 31);

        var (start, end) = McpToolExecutor.ParseDateRange(
            "2024-01-01",
            "2024-03-31",
            unusedDefaultStart
        );

        start.Should().Be(new DateOnly(2024, 1, 1));
        end.Should().Be(new DateOnly(2024, 3, 31));
    }
}
