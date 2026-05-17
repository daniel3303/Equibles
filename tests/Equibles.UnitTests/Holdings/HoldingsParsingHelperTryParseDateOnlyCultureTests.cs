using System.Globalization;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperTryParseDateOnlyCultureTests
{
    // Contract (XML-doc): "Parses date strings in both ISO (yyyy-MM-dd) and
    // SEC (dd-MMM-yyyy) formats." This is unconditional, so an ISO 13F date
    // must parse to the same Gregorian date on any host. ar-SA defaults to the
    // Umm al-Qura (Hijri) calendar — the first branch uses culture-sensitive
    // DateOnly.TryParse, so a Worker there must still read "2024-03-15" as
    // 15 March 2024, not a Hijri reinterpretation.
    [Fact(Skip = "GH-770 — TryParseDateOnly fails ISO dates under Hijri-calendar culture")]
    public void TryParseDateOnly_IsoDateUnderHijriCalendarCulture_ReturnsGregorianDate()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var success = HoldingsParsingHelper.TryParseDateOnly("2024-03-15", out var result);

            success.Should().BeTrue();
            result.Should().Be(new DateOnly(2024, 3, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
