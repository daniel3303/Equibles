using System.Globalization;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperParseDateCultureTests
{
    // Contract: ParseDate parses US congressional disclosure dates, which use
    // the US MM/dd/yyyy convention. "03/04/2025" is March 4, 2025 regardless
    // of the host machine's culture — a Worker deployed under en-GB/de-DE must
    // not silently read it as April 3.
    [Fact]
    public void ParseDate_UsFormatUnderNonUsCulture_InterpretsAsMonthDayYear()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");

            var result = DisclosureParsingHelper.ParseDate("03/04/2025");

            result.Should().Be(new DateOnly(2025, 3, 4));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
