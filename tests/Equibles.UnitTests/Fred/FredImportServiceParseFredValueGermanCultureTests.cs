using System.Globalization;
using System.Reflection;
using Equibles.Fred.HostedService.Services;

namespace Equibles.UnitTests.Fred;

public class FredImportServiceParseFredValueGermanCultureTests
{
    [Fact]
    public void ParseFredValue_DotDecimalUnderGermanCulture_ParsesNotMisreadAsThousandsSeparator()
    {
        // FredImportService.ParseFredValue is the per-observation entry point
        // for FRED's REST API — every observation value flows through here
        // before being persisted to FredObservation.Value. FRED's wire format
        // is ISO numeric with `.` as the decimal separator (e.g. `1.5`, `3.14`).
        // The German locale (de-DE) and many other European locales use `,`
        // as the decimal separator and treat `.` as the thousands separator.
        // Without an explicit `CultureInfo.InvariantCulture` argument,
        // `decimal.TryParse("1.5")` under de-DE thread culture returns `15m`
        // (i.e. "one thousand five hundred" — `.` consumed as a thousands
        // separator and the trailing 5 appended). Every FRED observation
        // would be silently multiplied by 10 (or worse, for values like
        // `1.234`, by 1000) on any Worker host whose locale defaults to a
        // comma-decimal culture.
        //
        // The risk this catches: a refactor that "tidies" the call to the
        // shorter `decimal.TryParse(raw, out var parsed)` (dropping both
        // the NumberStyles flag and the InvariantCulture argument) would
        // compile, pass every test that doesn't switch thread culture, and
        // corrupt every observation for any operator deploying under
        // de-DE / fr-FR / pt-PT / es-ES. The corruption is unrecoverable
        // without a re-import — the original wire string isn't preserved.
        //
        // Pin: under CultureInfo.CurrentCulture = de-DE, calling
        // ParseFredValue("1.5") must return 1.5m, NOT 15m. Wrap in
        // try/finally so the assertion failure path still restores the
        // original culture.
        var method = typeof(FredImportService).GetMethod(
            "ParseFredValue",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = (decimal?)method.Invoke(null, ["1.5"]);

            result.Should().Be(1.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
