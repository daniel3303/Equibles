using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

public class McpToolExecutorParseDateRangeFallbackTests
{
    // Contract: each bound "defaults to ..." when its text arg is absent or
    // unusable. A client-supplied non-date string must degrade to the supplied
    // defaultStart — never throw and never collapse to default(DateOnly)
    // (0001-01-01). A valid end is supplied so the assertion stays deterministic
    // and isolates the start-fallback branch the lone happy-path test never exercises.
    [Fact]
    public void ParseDateRange_UnparseableStart_FallsBackToDefaultStart()
    {
        var defaultStart = new DateOnly(2023, 6, 15);

        var (start, end) = McpToolExecutor.ParseDateRange("not-a-date", "2024-03-31", defaultStart);

        start.Should().Be(defaultStart);
        end.Should().Be(new DateOnly(2024, 3, 31));
    }
}
