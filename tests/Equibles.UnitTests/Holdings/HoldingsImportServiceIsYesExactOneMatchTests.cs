using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceIsYesExactOneMatchTests
{
    // IsYes maps SEC cover-page Y/N text to a bool — used for the
    // ConfidentialTreatmentRequested flag on 13F holdings. The OR chain
    // distinguishes between case-insensitive word matches (`Y`, `yes`,
    // `true`) and a strict equality check on `"1"`. A refactor that
    // generalised the `"1"` arm into `raw.StartsWith("1")` or
    // `raw.Contains("1")` — a plausible "simplification" — would silently
    // flip every numeric SEC payload starting with 1 (e.g. quantities like
    // "10000") into a confidential-treatment claim, mis-flagging the row
    // and (worse) hiding the real holding under the suppressed-output path.
    [Fact]
    public void IsYes_MultiDigitNumericLeadingOne_ReturnsFalse()
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "IsYes",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method.Invoke(null, ["10"]);

        result.Should().BeFalse();
    }
}
