using System.Globalization;
using System.Reflection;
using Equibles.Cftc.HostedService.Services;

namespace Equibles.UnitTests.Cftc;

public class CftcImportServiceParseDateCultureTests
{
    // CFTC report dates are ISO yyyy-MM-dd and must parse culture-independently. Under a
    // non-Gregorian host culture (ar-SA Umm al-Qura) a culture-sensitive parse would read "2024"
    // as a Hijri year and fail or mis-date the report. The invariant parse must recover the
    // Gregorian 2024-09-30. The existing format tests don't pin culture. Oracle from the contract.
    [Fact]
    public void ParseDate_IsoDateUnderUmmAlQuraCulture_ParsesGregorian()
    {
        var method = typeof(CftcImportService).GetMethod(
            "ParseDate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var result = (DateOnly?)method.Invoke(null, ["2024-09-30"]);

            result.Should().Be(new DateOnly(2024, 9, 30));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
