using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientParseInvariantDateOrUnparseableTests
{
    // ParseInvariantDateOr's contract is `parsed ?? fallback` — the caller
    // names the sentinel to use when SEC sends an unparseable date string
    // (truncated payload, blank cell, Hebrew/Hijri-formatted edge cases).
    // The fallback is what's actually written to the filing row, so the
    // honored value matters: callers commonly pass the filing's already-
    // known date (PeriodOfReport) so a parse failure doesn't poison the
    // record with DateOnly.MinValue. A refactor that returned
    // `default(DateOnly)` (= MinValue) for the failure arm — independent
    // of the fallback arg — would silently overwrite every recovery
    // path's intended sentinel with 0001-01-01.
    [Fact]
    public void ParseInvariantDateOr_UnparseableText_ReturnsTheSuppliedFallbackNotMinValue()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "ParseInvariantDateOr",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var fallback = new DateOnly(2024, 6, 30);

        var result = (DateOnly)method.Invoke(null, ["not-a-date", fallback]);

        result.Should().Be(fallback);
    }
}
