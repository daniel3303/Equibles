using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceIsYesNullGuardTests
{
    [Fact]
    public void IsYes_NullInput_ReturnsFalseWithoutThrowing()
    {
        // Sibling to the four IsYes truthy-arm pins (Y, yes, true, "1") and
        // the exact-one rejection. The `!IsNullOrEmpty(raw) && (...)`
        // short-circuit at the top of IsYes is the load-bearing guard
        // against an NRE: every truthy arm calls `raw.Equals(...)` and
        // null.Equals throws NullReferenceException. A refactor that drops
        // the IsNullOrEmpty short-circuit (e.g. "the caller always passes
        // a value") would compile, pass every existing pin (all pass
        // non-null strings), and NRE the first time a 13F cover page omits
        // CONFIDENTIALTREATMENT entirely — taking down the quarterly
        // holdings import. Pin: IsYes(null) returns false, no throw.
        var method = typeof(HoldingsImportService).GetMethod(
            "IsYes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, [(object)null]);

        result.Should().BeFalse();
    }
}
