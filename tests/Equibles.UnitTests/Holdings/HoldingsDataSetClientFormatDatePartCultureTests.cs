using System.Globalization;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// FormatDatePart builds SEC URL date segments like "01mar2024". The day and
/// month already use InvariantCulture, but the year uses bare ToString("yyyy")
/// which emits Hijri years on ar-SA threads — producing URLs that 404.
/// </summary>
public class HoldingsDataSetClientFormatDatePartCultureTests
{
    [Fact]
    public void FormatDatePart_HijriCultureThread_EmitsGregorianYear()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var result = HoldingsDataSetClient.FormatDatePart(new DateOnly(2024, 3, 1));

            result.Should().Be("01mar2024", "year must be Gregorian, not Hijri");
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }
}
