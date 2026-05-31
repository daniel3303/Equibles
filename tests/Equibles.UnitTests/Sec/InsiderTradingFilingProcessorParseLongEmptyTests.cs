using System.Reflection;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseLongEmptyTests
{
    // Sibling to ParseLongDirectSuccess, ParseLongOverflow,
    // ParseLongNegativeOverflow, and the decimal-string fallback test
    // in the main InsiderTradingFilingProcessorTests file. The null/
    // empty early-return guard at the top of ParseLong — `if
    // (string.IsNullOrEmpty(value)) return 0;` — is unpinned. The
    // ParseBool sibling already pins ParseBool's null path; ParseLong's
    // guard is structurally distinct (explicit `IsNullOrEmpty` check vs
    // ParseBool's `value?.Trim()` null-conditional chain), so the
    // ParseBool pin doesn't transitively protect this one.
    //
    // Empty/null is production-real: ParseLong is invoked on
    // `Wrapped("transactionAmounts", "transactionShares")` chains that
    // return null when any intermediate element is missing (a Form 4
    // holding row without a transactionShares wrapper, an amendment
    // that omits the optional post-transaction-amounts block, etc.).
    //
    // The risks this pin uniquely catches and the sibling overflow
    // pins cannot:
    //   • Drop the `IsNullOrEmpty` guard — `long.TryParse("")` returns
    //     false (no NRE), so falls through to `ParseDecimal("")`. That
    //     guard ALSO has `IsNullOrEmpty`, so still returns 0. But a
    //     refactor that drops BOTH guards under "DefaultIfEmpty"
    //     reasoning would NRE on the path that calls
    //     `Replace(",", "")` after long.TryParse fails — wait, the
    //     production code doesn't do that. Let me reconsider.
    //   • The more direct catch: a refactor that throws on empty input
    //     (`if (string.IsNullOrEmpty(value)) throw new
    //     ArgumentException(...);`) would compile and crash the worker
    //     on every Form 4 with an optional-empty field. This pin
    //     catches it.
    //   • An inversion (`if (!string.IsNullOrEmpty(value)) return 0;`)
    //     — would also pass the existing siblings (they all supply
    //     non-empty input) and silently zero out every Form 4 with a
    //     valid number. Asserting empty → 0 is the same observable;
    //     this pin alone wouldn't catch the inversion.
    //
    // Pin: ParseLong("") returns 0 without throwing. Dual assertion
    // (`.NotThrow` + `.Be(0L)`) defends against the throw regression.
    [Fact]
    public void ParseLong_EmptyInput_ReturnsZeroWithoutThrowing()
    {
        var method = typeof(InsiderFilingParser).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        long result = -1;
        var act = () => result = (long)method.Invoke(null, [""]);

        act.Should().NotThrow();
        result.Should().Be(0L);
    }
}
