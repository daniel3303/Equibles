using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

public class McpToolExecutorStockNotFoundFormatTests
{
    // Contract (McpToolExecutor.cs:10):
    //   public static string StockNotFound(string ticker)
    //       => $"Stock '{ticker}' not found.";
    //
    // This is the canonical "ticker not in our database" message returned
    // by every MCP tool that looks up a CommonStock by ticker — the
    // negative response shape every LLM consumer is documented to receive
    // when the requested ticker doesn't exist. The literal includes
    // single-quotes wrapping the ticker AND the trailing period AND
    // capitalised "Stock" — three structural elements that the LLM
    // prompt-engineering layer relies on for downstream message parsing.
    //
    // Existing tests:
    //   • Many integration tests in tests/Equibles.IntegrationTests/Mcp/
    //     invoke MCP tools with unknown tickers and assert the result
    //     `.Contain("not found")` or similar substring matches. None
    //     asserts the EXACT format string. A regression that dropped
    //     the single-quotes, changed "Stock" to "Ticker", or removed
    //     the trailing period would PASS every existing pin while
    //     silently changing the wire format every MCP client receives.
    //   • No unit pin exists for the helper itself.
    //
    // The risks this pin uniquely catches:
    //
    //   • Quote-style drift — `$"Stock \"{ticker}\" not found."` from
    //     a refactor that "matched the convention of error messages"
    //     would compile and pass every `.Contain("not found")` style
    //     integration test. LLM prompts that key on the single-quote
    //     pattern (e.g. a downstream "extract the ticker from the
    //     error message" heuristic) would silently fail to match.
    //
    //   • Trailing-period drop — `$"Stock '{ticker}' not found"` from
    //     a "sentence punctuation is inconsistent across messages"
    //     cleanup. Some MCP clients render each message on its own
    //     line and rely on terminal punctuation for visual cohesion.
    //
    //   • Capitalisation swap — `"stock"` lowercase, or "Ticker"
    //     instead of "Stock". The wire format is documented at the
    //     LLM prompt layer.
    //
    //   • Ticker-substitution drop — a copy-paste from a sibling
    //     helper that emitted a hardcoded message without the
    //     interpolation. Asserting on a specific ticker value
    //     ("AAPL") and matching it in the output catches a
    //     dropped substitution (result would not contain "AAPL").
    //
    // Pin: invoke `StockNotFound("AAPL")` and assert the exact
    // string `"Stock 'AAPL' not found."`. The exact-equality
    // assertion catches every regression class in one shot:
    //   • Quote drop / swap → string differs at quote chars.
    //   • Trailing period drop → string differs in length.
    //   • Capitalisation swap → exact char mismatch.
    //   • Dropped ticker substitution → "AAPL" missing → fail.
    //   • Different word for "Stock" → mismatch.
    [Fact]
    public void StockNotFound_KnownTicker_ReturnsSingleQuotedTickerWithTrailingPeriod()
    {
        var result = McpToolExecutor.StockNotFound("AAPL");

        result.Should().Be("Stock 'AAPL' not found.");
    }
}
