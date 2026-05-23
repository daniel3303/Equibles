using System.Globalization;
using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// FormatDatePart uses InvariantCulture for day and month but omits it for
/// the year component. On non-Gregorian calendar threads (e.g. ar-SA /
/// Hijri), ToString("yyyy") produces the Hijri year, corrupting the SEC
/// download URL and causing a silent 404.
/// </summary>
public class HoldingsDataSetClientFormatDatePartCultureTests
{
    [Fact(Skip = "GH-1897 — FormatDatePart emits Hijri year on non-Gregorian culture threads")]
    public void FormatDatePart_HijriCultureThread_EmitsGregorianYear()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var method = typeof(HoldingsDataSetClient).GetMethod(
                "FormatDatePart",
                BindingFlags.NonPublic | BindingFlags.Static
            );

            var result = (string)method!.Invoke(null, [new DateOnly(2024, 3, 1)]);

            result.Should().Be("01mar2024", "year must be Gregorian, not Hijri");
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }
}
