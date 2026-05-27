using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

public class McpToolExecutorParseDateOrUnparseableTests
{
    [Fact]
    public void ParseDateOr_UnparseableNonEmptyText_ReturnsFallbackWithoutThrowing()
    {
        // McpToolExecutor.ParseDateOr is the date-arg gate every MCP tool
        // uses to coerce string-typed `fromDate` / `toDate` args into a
        // DateOnly with a fallback. Contract: `!IsNullOrEmpty(text) &&
        // TryParse → parsed; else fallback`. The LLM routinely sends
        // not-quite-ISO strings ("Jan 15, 2024", "yesterday", "Q1 2024")
        // — these are non-empty but unparseable. The helper must drop them
        // into the fallback, not throw. A refactor that drops the
        // `TryParse` and uses `Parse` (or that drops the AND short-circuit)
        // would NRE/throw on every malformed LLM date input — taking down
        // the tool execution chain. Pin: a clearly-malformed string
        // returns the fallback. The integration tests don't isolate this
        // helper, and the existing `McpToolExecutorTests` covers only
        // Execute().
        var fallback = new DateOnly(2099, 1, 1);

        var result = McpToolExecutor.ParseDateOr("not a date", fallback);

        result.Should().Be(fallback);
    }
}
