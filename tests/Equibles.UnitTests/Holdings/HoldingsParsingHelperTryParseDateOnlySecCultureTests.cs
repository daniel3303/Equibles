using System.Globalization;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperTryParseDateOnlySecCultureTests
{
    // Contract (XML-doc): "Parses ... SEC (dd-MMM-yyyy) format" — unconditional,
    // so it must hold on any host. The existing culture test only pins the ISO
    // leg under ar-SA (Umm al-Qura); the SEC leg parses an English month
    // abbreviation ("SEP") and only works because it passes InvariantCulture.
    // Under ar-SA, a current-culture parse of "SEP" fails (Arabic month names),
    // so a refactor dropping InvariantCulture from the TryParseExact leg would
    // break SEC 13F dates on non-English-month hosts while every en/invariant
    // test stayed green. Pin the SEC leg under the documented hostile culture.
    [Fact]
    public void TryParseDateOnly_SecFormatUnderHijriCalendarCulture_ReturnsGregorianDate()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var success = HoldingsParsingHelper.TryParseDateOnly("30-SEP-2024", out var result);

            success.Should().BeTrue();
            result.Should().Be(new DateOnly(2024, 9, 30));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
