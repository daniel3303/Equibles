using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpLimitClampNegativeCapTests
{
    // Contract: McpLimit.Clamp exists so a non-positive result cap never reaches
    // .Take(...) as a negative SQL LIMIT (PostgreSQL rejects it as an internal
    // error) and never yields zero rows (zero rows make a tool render a factual
    // empty-state message even when the subject has data). A non-positive cap
    // clamps to the floor of 1. int.MinValue is the extreme boundary: it must
    // still clamp to 1, not overflow or pass a negative value through.
    [Fact]
    public void Clamp_IntMinValue_ReturnsFloorNotNegative()
    {
        var result = McpLimit.Clamp(int.MinValue);

        result.Should().Be(1);
    }

    // Contract: an oversized cap (e.g. int.MaxValue) must be bounded so it never flows
    // into .Take(...) and pulls an enormous result set, exhausting memory and DB time.
    // Clamp caps at McpLimit.MaxResults.
    [Fact]
    public void Clamp_IntMaxValue_ReturnsMaxResultsCap()
    {
        var result = McpLimit.Clamp(int.MaxValue);

        result.Should().Be(McpLimit.MaxResults);
    }

    // A non-positive cap clamps up to the floor of 1; values within
    // [1, MaxResults] pass through unchanged.
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(McpLimit.MaxResults, McpLimit.MaxResults)]
    public void Clamp_WithinRange_ReturnsInput(int input, int expected)
    {
        var result = McpLimit.Clamp(input);

        result.Should().Be(expected);
    }
}
