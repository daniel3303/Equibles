using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceIsYesYesArmTests
{
    [Fact]
    public void IsYes_MixedCaseYes_ReturnsTrueViaCaseInsensitiveMatch()
    {
        // Closes the IsYes truthy-arm family alongside the existing YArm
        // (`Y` canonical), `1` strict-equality, and just-added "true"
        // (lowercase) pins. The "yes" arm is the third textual alternative
        // — natural-language XBRL cover pages occasionally emit the full
        // word. A refactor that drops `Equals("yes", OrdinalIgnoreCase)`
        // would compile, pass each other sibling, and silently misclassify
        // any cover page whose authoring tool serialized the bool as the
        // word "yes" instead of "Y" / "true" / "1". Pin MIXED case ("Yes")
        // so the case-insensitive comparator is also exercised — a swap to
        // `Ordinal` Equals would only match "yes" lowercase and fail here.
        var method = typeof(HoldingsImportService).GetMethod(
            "IsYes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, ["Yes"]);

        result.Should().BeTrue();
    }
}
