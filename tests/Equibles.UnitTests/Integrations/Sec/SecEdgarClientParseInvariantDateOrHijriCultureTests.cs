using System.Globalization;
using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientParseInvariantDateOrHijriCultureTests
{
    [Fact]
    public void ParseInvariantDateOr_IsoDateUnderHijriCulture_ReturnsParsedNotFallback()
    {
        // SecEdgarClient.ParseInvariantDateOr's inline doc-comment states:
        //   "SEC submissions feed dates are ISO yyyy-MM-dd. Parse them
        //    culture-independently — under a non-Gregorian host culture
        //    (e.g. ar-SA Umm al-Qura) culture-sensitive TryParse fails
        //    and every filing would be stamped with the fallback."
        //
        // MapToFilingData calls the helper for both FilingDate and ReportDate
        // on every filing returned by SEC's submissions JSON. Under a Hijri
        // host culture, a regression that drops the explicit
        // `CultureInfo.InvariantCulture` argument (e.g. tidied to the
        // shorter `DateOnly.TryParse(text, out var parsed)`) would compile,
        // pass every existing test (none probe this helper, none run under
        // a non-Gregorian culture), and silently stamp DateOnly.MinValue on
        // every single filing for any Worker host whose locale defaults to
        // ar-SA. The downstream LinkedFilings queries then return nothing
        // because no filing date matches any reasonable lookback window.
        //
        // Pin the contract: under ar-SA Umm al-Qura thread culture, the
        // ISO string "2024-02-01" must parse to DateOnly(2024, 2, 1), NOT
        // the fallback sentinel. Wrap in try/finally so the suite restores
        // the original culture even if the assertion fires.
        var method = typeof(SecEdgarClient).GetMethod(
            "ParseInvariantDateOr",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var fallback = DateOnly.MinValue;
            var result = (DateOnly)method.Invoke(null, ["2024-02-01", fallback]);

            result.Should().Be(new DateOnly(2024, 2, 1));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
