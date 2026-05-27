using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseBoolNullTests
{
    // Sibling to ParseBoolLowercaseTrueTests and ParseBoolWhitespacePaddedTests
    // (the two existing positive arms). The null-safe-fallback path —
    // `value?.Trim() is ...` — is unpinned. The `?.` null-conditional short-
    // circuits a null `value` to `null`, and pattern-matching against the
    // string literals returns false. The body relies on that elision
    // entirely; a refactor that dropped the `?.` (e.g. inlined the Trim
    // call: `value.Trim() is ...`) would NRE on any null upstream input.
    //
    // Null is production-real here: ParseBool is invoked on
    // `ownerRelationship?.Element(...)?.Value` chains. Every missing
    // `isDirector` / `isOfficer` / `isTenPercentOwner` element flows
    // through as null. SEC Form 4 omits these elements for officers whose
    // role doesn't apply (a 10% owner who isn't an officer or director
    // has only `isTenPercentOwner = true`; the other two elements are
    // absent). Dropping the `?.` would crash the parser on every such
    // filing and lose every 10%-owner-only insider's transactions.
    //
    // The risks this pin uniquely catches and the two existing siblings
    // cannot:
    //   • Drop the `?.` — would NRE on null; siblings pass non-null.
    //   • Inversion (`return value?.Trim() is not (...)`) — siblings
    //     would FAIL (positive cases) but the inversion regression also
    //     causes null → true, the assertion here catches it.
    //   • A "tighten" refactor that adds `?? string.Empty` would pass
    //     this pin (null → "" → no match → false) without exposing a
    //     defensive shift in convention.
    //
    // Pin: ParseBool(null) returns false.
    [Fact]
    public void ParseBool_NullInput_ReturnsFalseWithoutThrowing()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseBool",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        bool result = false;
        var act = () => result = (bool)method.Invoke(null, [null]);

        act.Should().NotThrow();
        result.Should().BeFalse();
    }
}
