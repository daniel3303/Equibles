using System.Globalization;
using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsExportControllerFormatNullablePercentInvariantCultureTests
{
    // The existing pin covers the null arm (→ empty). This pins the HasValue arm:
    // a present value formats with F2 and InvariantCulture. The invariant separator
    // is load-bearing on this CSV hot path — under a comma-decimal host culture a
    // non-invariant format would emit "12,50", injecting a field delimiter into the
    // comma-separated row and corrupting the export. Asserted under de-DE.
    [Fact]
    public void FormatNullablePercent_ValueUnderCommaDecimalCulture_UsesInvariantPeriodSeparator()
    {
        var method = typeof(HoldingsExportController).GetMethod(
            "FormatNullablePercent",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var result = (string)method!.Invoke(null, [(double?)12.5]);

            result.Should().Be("12.50");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }
}
