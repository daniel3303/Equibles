using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

public class McpToolExecutorParseDateOrParseableTextTests
{
    // Contract (McpToolExecutor.ParseDateOr, single expression):
    //   !string.IsNullOrEmpty(text) && DateOnly.TryParse(text, out var parsed)
    //       ? parsed : fallback
    // Three reachable outcomes:
    //   • Parseable non-empty text → parsed                ← THIS pin (UNPINNED)
    //   • Unparseable non-empty text → fallback            ← pinned by sibling
    //   • Null/empty text → fallback (IsNullOrEmpty short) ← UNPINNED
    //
    // The parseable-text arm is the production happy path — every
    // successful MCP tool call with a valid `fromDate` / `toDate`
    // string argument flows through this arm and gets the parsed
    // DateOnly back. The unparseable-text sibling asserts that
    // malformed input falls through; it does NOT assert that valid
    // input round-trips into the result.
    //
    // The risks this pin uniquely catches:
    //
    //   • "Always-return-fallback" regression: a refactor that
    //     `return fallback;` short-circuited the entire method
    //     (under a "we should validate dates at the caller, not
    //     here" cleanup) would compile, pass the unparseable-text
    //     sibling (both inputs return fallback), and silently
    //     replace EVERY MCP date arg with the caller's default —
    //     `GetStockPrices(ticker, fromDate: "2024-01-01")` would
    //     return all-time prices instead of YTD.
    //
    //   • Inverted ternary: `: parsed ? fallback` (the colon-question
    //     swap from a typo'd refactor) would compile and the
    //     unparseable sibling would still see fallback (because
    //     the AND evaluates false), but the parseable arm would
    //     swap to fallback — every valid date silently becomes
    //     the default.
    //
    //   • Dropped TryParse: `!IsNullOrEmpty(text) ? DateOnly.Parse(text) : fallback`
    //     would compile cleanly and throw FormatException on
    //     unparseable inputs (caught by the sibling). On parseable
    //     inputs it would still work — but the failure mode
    //     differs (throw vs. fallback) on unparseable. Neither
    //     pin would catch this drop on parseable input alone.
    //     Still, the parseable pin pins the happy-path return
    //     value, so any value-mangling swap surfaces here.
    //
    //   • Out-param mishandling: a regression that returned
    //     `default` instead of `parsed` (`DateOnly.TryParse` is
    //     called for its bool side effect but `parsed` is
    //     discarded) would compile and return default(DateOnly)
    //     for every successful parse. Asserting on the exact
    //     parsed value catches this.
    //
    // Pin: ParseDateOr("2024-03-15", fallback) returns
    // new DateOnly(2024, 3, 15) where `fallback` is a distinct,
    // unmistakable date (1999-12-31). The DUAL "fallback isn't
    // returned AND parsed value matches" assertion is the
    // minimal pin that catches all three regression classes.
    [Fact]
    public void ParseDateOr_ParseableIsoDateText_ReturnsParsedDateNotFallback()
    {
        var fallback = new DateOnly(1999, 12, 31);

        var result = McpToolExecutor.ParseDateOr("2024-03-15", fallback);

        result.Should().Be(new DateOnly(2024, 3, 15));
        result.Should().NotBe(fallback);
    }
}
