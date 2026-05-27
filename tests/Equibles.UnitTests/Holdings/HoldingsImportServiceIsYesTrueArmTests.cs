using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceIsYesTrueArmTests
{
    [Fact]
    public void IsYes_LowercaseTrue_ReturnsTrueViaCaseInsensitiveMatch()
    {
        // Sibling to IsYesYArmTests and IsYesExactOneMatchTests. Those pin
        // the "Y" canonical arm and the "1" strict-equality arm respectively.
        // IsYes's four-arm OR also recognises "yes" and "true" as truthy
        // (case-insensitive), to handle XBRL-cover-page payloads that
        // occasionally emit a boolean-style string. The "true" arm is
        // unpinned. A refactor that "tightens" the predicate to only the
        // canonical SEC encodings (`Y` / `1`) would compile, pass both
        // existing pins, and misclassify every cover page whose authoring
        // tool serialized the bool as the word "true" — silently dropping
        // those into the non-confidential bucket. Pin the LOWERCASE
        // "true" so the case-insensitive comparison is also exercised
        // (uppercase or "True" would still match an Ordinal `Equals`).
        var method = typeof(HoldingsImportService).GetMethod(
            "IsYes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, ["true"]);

        result.Should().BeTrue();
    }
}
