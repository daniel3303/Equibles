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
    // IsYes maps SEC cover-page CONFIDENTIALTREATMENT text to a bool via a
    // four-way OR chain — `Equals("Y", OrdinalIgnoreCase) || Equals("yes", …)
    // || Equals("true", …) || raw == "1"`. The "Y" arm is the DOMINANT
    // production payload: Realtime13FArchiveBuilder hard-codes
    // `filing.ConfidentialTreatmentRequested ? "Y" : "N"` (line 60), so every
    // realtime-ingested 13F-HR holding's cover-page row carries "Y" / "N" —
    // never the textual variants. SEC's bulk 13F-HR feed also predominantly
    // uses "Y" / "N" in CONFIDENTIALTREATMENT.
    //
    // The risk this catches that no other test does: a refactor that drops
    // the `"Y"` arm of the OR chain (perhaps under the reasoning "the 'true'
    // / 'yes' arms cover the common cases; 'Y' is just a single-letter
    // shorthand") would compile cleanly, pass the existing exact-one-match
    // pin (still returns false on "10"), and silently misclassify EVERY
    // confidential-treatment 13F-HR holding from confidential (suppressed
    // from public dashboards per SEC's confidential-treatment framework)
    // to non-confidential (publicly visible). The disclosure failure mode
    // is invisible — the import logs "Upserted N institutional holders"
    // without an error; downstream queries that filter on
    // ConfidentialTreatmentRequested simply return the suppressed positions.
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
