using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperParseNullableIntUnparseableInputTests
{
    // ParseNullableInt is one of the two unpinned parsing helpers in
    // HoldingsParsingHelper (sibling: ParseLong). Its contract is the
    // distinction the `Nullable<int>` return type promises:
    //     int.TryParse(value, out var result) ? result : null
    // — UNPARSEABLE input returns `null`, NOT `0`. The `null` signal
    // is load-bearing: the 13F XML import pipeline uses it to mean
    // "missing/unreported value", while `0` would mean "the issuer
    // explicitly reported zero" (a meaningful business signal — e.g.
    // a fund that exited every position would report 0 shares).
    //
    // The risks this pin uniquely catches:
    //
    //   • Wrong-fallback regression — `ParseLong`-style return:
    //     `int.TryParse(value, out var result) ? result : 0` — would
    //     compile (the return type narrows from int? to int and
    //     loses the null), silently conflate "missing" with
    //     "reported zero". Holdings dashboards would show zero
    //     positions where 13F XML had a malformed sequence-number
    //     attribute instead of surfacing the parse failure.
    //
    //   • int.Parse swap — `int.Parse(value)` (under "TryParse is
    //     defensive overkill") would throw FormatException on every
    //     malformed input. The Holdings13F bulk-import would fail
    //     mid-batch instead of skipping bad rows.
    //
    //   • Culture-sensitivity regression — int.TryParse's default
    //     NumberStyles.Integer rejects thousand separators, so it
    //     is culture-insensitive for clean integer input. But a
    //     refactor that broadens to NumberStyles.AllowThousands +
    //     CurrentCulture would PARSE "1,234" as 1234 under en-US
    //     and FAIL under fr-FR (NBSP separator). This pin uses
    //     a clearly non-integer input ("abc") that fails under any
    //     style — defending the rejection-fallback contract, not
    //     the parsing contract itself.
    //
    // Adversarial input: "abc" — clearly unparseable, exercises the
    // null-fallback arm directly. Dual assertion (the method's
    // return value AND its nullness) distinguishes:
    //   • Working: returns null.
    //   • Wrong-fallback regression (→ 0): returns 0, fails BeNull.
    //   • int.Parse swap: throws FormatException, surfaces through
    //     reflection's TargetInvocationException.
    [Fact]
    public void ParseNullableInt_UnparseableInput_ReturnsNullNotZero()
    {
        var method = typeof(HoldingsParsingHelper).GetMethod(
            "ParseNullableInt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (int?)method!.Invoke(null, ["abc"]);

        result
            .Should()
            .BeNull(
                "null signals missing/unreported; 0 would conflate with a real reported-zero value"
            );
    }
}
