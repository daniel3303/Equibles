using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceIsYesYArmTests
{
    // Sibling to HoldingsImportServiceIsYesExactOneMatchTests. That pin asserts
    // IsYes("10") → false, protecting the "1" arm's STRICT equality semantic
    // from a widening refactor (Contains/StartsWith). This pin asserts the
    // complementary positive case: IsYes("Y") → true, protecting the dominant
    // truthy arm from a drop.
    //
    // IsYes maps SEC Y/N text, such as SUMMARYPAGE.ISCONFIDENTIALOMITTED, to a bool via a
    // four-way OR chain. "Y" is the dominant payload: Realtime13FArchiveBuilder writes
    // `filing.ConfidentialOmitted ? "Y" : "N"`, so dropping this arm would silently clear the
    // confidential-treatment warning for every realtime-ingested filer.
    //
    // Pin "Y" (uppercase, the canonical SEC wire encoding) via reflection
    // on the private static IsYes — the same pattern the existing exact-
    // one-match sibling uses. The pair together pins:
    //   • The "1" arm's strict-equality semantic (existing pin, "10" → false)
    //   • The "Y" arm's truthy mapping (this pin, "Y" → true)
    // A drop of the "Y" arm surfaces here. A widening of the "1" arm
    // surfaces in the existing sibling.
    [Fact]
    public void IsYes_UppercaseY_ReturnsTrue()
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "IsYes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, ["Y"]);

        result.Should().BeTrue();
    }
}
