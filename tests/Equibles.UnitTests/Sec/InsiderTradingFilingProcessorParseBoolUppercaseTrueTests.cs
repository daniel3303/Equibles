using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseBoolUppercaseTrueTests
{
    // Fourth sibling in the ParseBool family. The other three pin: " 1 "
    // (whitespace+numeric), "true" (lowercase xsd:boolean), and null (the
    // null-safe-fallback). The contract `value?.Trim() is "1" or "true"
    // or "True" or "TRUE"` enumerates FOUR truthy literals; the uppercase
    // "TRUE" arm — the canonical form emitted by some SEC Form 4 filings
    // that originate from spreadsheet exports (Excel's UPPER() on a
    // boolean column) — is unpinned.
    //
    // The risks this pin uniquely catches:
    //   • Drop of the "TRUE" arm under a "simplify" refactor that
    //     collapses to `value?.Trim().ToLowerInvariant() is "1" or
    //     "true"`. The xsd:boolean lowercase sibling still passes
    //     ("true" → matches "true" after toLower). But under the
    //     ORIGINAL strict-case implementation, dropping the arm means
    //     "TRUE" → no match → false. Pinning "TRUE" → true defends
    //     against an asymmetric drop.
    //   • A "harmonize via OrdinalIgnoreCase" refactor that changes
    //     matching semantics — would still satisfy this pin (passes
    //     for "TRUE") AND the lowercase pin, but the convention shift
    //     (strict-case → case-insensitive) is a quiet contract
    //     change. This pin alone wouldn't catch the convention shift;
    //     it does catch the more-likely "drop arm" refactor.
    //
    // Pin: ParseBool("TRUE") returns true.
    [Fact]
    public void ParseBool_UppercaseTrue_ReturnsTrue()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseBool",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method.Invoke(null, ["TRUE"]);

        result.Should().BeTrue();
    }
}
