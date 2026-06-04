using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

public class McpToolExecutorParseDateRangeEndFallbackTests
{
    // Contract asymmetry: an unparseable START degrades to the supplied
    // defaultStart, but an unparseable END degrades to TODAY (UTC) — the
    // open-ended "...through now" default. The start-fallback pin passes a valid
    // end and never exercises this arm; a regression copy-pasting defaultStart
    // into the end slot would silently end every open range in the past.
    [Fact]
    public void ParseDateRange_UnparseableEnd_FallsBackToTodayNotDefaultStart()
    {
        var defaultStart = new DateOnly(2000, 1, 1);
        var before = DateOnly.FromDateTime(DateTime.UtcNow);

        var (start, end) = McpToolExecutor.ParseDateRange("2024-03-31", "not-a-date", defaultStart);

        var after = DateOnly.FromDateTime(DateTime.UtcNow);
        start.Should().Be(new DateOnly(2024, 3, 31));
        end.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}
