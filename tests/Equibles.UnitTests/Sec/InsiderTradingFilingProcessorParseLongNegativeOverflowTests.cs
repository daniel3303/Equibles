using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseLongNegativeOverflowTests
{
    private static readonly MethodInfo ParseLongMethod =
        typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Symmetric sibling of the GH-1673 positive-overflow pin. The just-shipped
    // clamp is `d > long.MaxValue || d < long.MinValue ? 0 : (long)d`; a
    // refactor that drops the `< long.MinValue` half (or "tidies" the OR into
    // a single Math.Abs comparison that mishandles the sign) would compile,
    // pass the positive-overflow pin, and silently re-introduce the crash for
    // any dirty Form 4 carrying a digit-only negative number below
    // long.MinValue (-9.22e18). Pin the matching negative branch so both
    // bounds are locked: ParseLong must return 0 (no throw) on any input
    // that overflows the long range, regardless of sign.
    [Fact]
    public void ParseLong_NegativeOverflowDecimalString_DoesNotThrowAndReturnsZero()
    {
        // -19 nines = -9_999_999_999_999_999_999 ≈ -1.0e19, strictly below
        // long.MinValue (~-9.22e18) but well inside decimal's range. Picks the
        // exact branch: long.TryParse rejects, decimal.TryParse accepts, and
        // without the clamp the cast would overflow.
        long result = 0;
        var act = () => result = (long)ParseLongMethod.Invoke(null, ["-9999999999999999999"]);

        act.Should().NotThrow();
        result.Should().Be(0);
    }
}
