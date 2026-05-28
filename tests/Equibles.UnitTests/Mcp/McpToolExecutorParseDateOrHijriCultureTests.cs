using System.Globalization;
using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

public class McpToolExecutorParseDateOrHijriCultureTests
{
    // ParseDateOr is the date-arg gate every MCP tool uses to coerce a
    // string `fromDate` / `toDate` into a DateOnly, with a fallback for
    // missing/unparseable input. Every date in the system is ISO
    // (`yyyy-MM-dd`), so a valid ISO string must round-trip to the same
    // DateOnly on any host — the InvariantCulture precedent already pinned
    // for the sibling parse helpers (Fred GH-1501, Holdings, Disclosure) and
    // for NormalizeTicker (ToUpperInvariant, same file). The implementation
    // calls `DateOnly.TryParse(text, out parsed)` with no explicit
    // InvariantCulture, so it parses against the thread calendar. Under
    // ar-SA (Umm al-Qura) the ISO date does not round-trip — the parse
    // fails to yield 2024-01-15 and the helper silently substitutes the
    // caller's fallback (and on full-ICU hosts the underlying calendar
    // conversion can throw ArgumentOutOfRangeException out of TryParse).
    // Either way, on a Hijri-locale host every MCP `fromDate` / `toDate`
    // is wrong: a request for a specific window quietly collapses to the
    // default range. The sibling unparseable-input test pins "returns
    // fallback"; this pins that a *valid* ISO date round-trips regardless
    // of host culture.
    [Fact]
    public void ParseDateOr_IsoDateUnderHijriCulture_ReturnsParsedDate()
    {
        var fallback = new DateOnly(1999, 12, 31);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var result = McpToolExecutor.ParseDateOr("2024-01-15", fallback);

            result.Should().Be(new DateOnly(2024, 1, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
