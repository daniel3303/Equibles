using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpLimitClampNegativeCapTests
{
    // Contract: McpLimit.Clamp exists so a negative result cap never reaches
    // .Take(...) as a negative SQL LIMIT (PostgreSQL rejects it as an internal
    // error). The documented promise is that a non-positive cap yields zero
    // rows, i.e. the clamp returns a non-negative value — 0 for any negative
    // input. int.MinValue is the extreme boundary: it must still clamp to 0,
    // not overflow or pass a negative value through.
    [Fact]
    public void Clamp_IntMinValue_ReturnsZeroNotNegative()
    {
        var result = McpLimit.Clamp(int.MinValue);

        result.Should().Be(0);
    }
}
