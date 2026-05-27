using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperParseLongUnparseableInputTests
{
    // Sibling to HoldingsParsingHelperParseNullableIntUnparseableInputTests
    // (which pins the int? null-fallback). ParseLong is the other unpinned
    // helper in HoldingsParsingHelper:
    //     long.TryParse(value, out var result) ? result : 0
    //
    // The contract distinction from ParseNullableInt is INTENTIONAL: where
    // ParseNullableInt returns null for unparseable input (signalling
    // "missing"), ParseLong returns 0. The 0 sentinel makes sense here
    // because ParseLong is used in the 13F XML import for SHARE counts
    // and VALUE figures — fields the 13F XML schema requires every row
    // to populate. A malformed value indicates a corrupted feed, NOT a
    // missing field; the 0 sentinel lets the import skip that row
    // numerically (the downstream pipeline filters rows with both
    // shares==0 AND value==0) without crashing the batch.
    //
    // The risks this pin uniquely catches:
    //
    //   • Wrong-sentinel regression — `... ? result : -1` or
    //     `... ? result : long.MinValue` (under "0 means a valid
    //     zero, use a clearer sentinel") would compile, and every
    //     downstream filter that checks `shares > 0` would treat
    //     the negative sentinel as "the filer holds a negative
    //     position" — possible in 13F-HR option-grant rows but
    //     not in share-count rows. The MinValue case would surface
    //     as ridiculous negative figures in the holdings dashboard.
    //
    //   • long.Parse swap — `long.Parse(value)` would throw
    //     FormatException on every malformed input, crashing the
    //     entire 13F-HR bulk-import batch (a single corrupted row
    //     would abort the whole filing's ingestion).
    //
    //   • Return-type narrowing — a refactor that changed the
    //     return type to `long?` to match the sibling helper's
    //     shape would break callers that assign the return to a
    //     non-nullable `long`. The compiler catches this, but a
    //     fix-up cleanup might "fix" callers to use `.Value` or
    //     `?? 0L` — converging the two helpers' shapes and losing
    //     the deliberate semantic distinction.
    //
    // Pin: `ParseLong("abc")` returns `0L`. The exact-value assertion
    // distinguishes:
    //   • Working: returns 0L.
    //   • Wrong-sentinel (-1, MinValue, etc.): fails on Should().Be(0L).
    //   • Parse-throw regression: TargetInvocationException at invoke.
    [Fact]
    public void ParseLong_UnparseableInput_ReturnsZeroSentinelNotNegativeOrMinValue()
    {
        var method = typeof(HoldingsParsingHelper).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (long)method!.Invoke(null, ["abc"]);

        result
            .Should()
            .Be(
                0L,
                "0 is the deliberate corrupted-row sentinel — downstream filters drop shares==0 rows numerically without aborting the bulk import"
            );
    }
}
