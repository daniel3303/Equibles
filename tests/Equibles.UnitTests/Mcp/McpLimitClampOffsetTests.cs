using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpLimitClampOffsetTests
{
    // Contract: a negative offset would flow into .Skip(...) as a negative SQL OFFSET; it
    // clamps to 0 (the "from the top" default). int.MinValue must not overflow through.
    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(McpLimit.MaxOffset, McpLimit.MaxOffset)]
    public void ClampOffset_ClampsToValidRange(int input, int expected)
    {
        McpLimit.ClampOffset(input).Should().Be(expected);
    }

    // An unbounded offset is a resource-exhaustion vector (the database skip-scans that many
    // rows), so it caps at MaxOffset like the REST host's paging.
    [Fact]
    public void ClampOffset_IntMaxValue_CapsAtMaxOffset()
    {
        McpLimit.ClampOffset(int.MaxValue).Should().Be(McpLimit.MaxOffset);
    }
}
