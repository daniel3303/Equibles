using System.Globalization;
using System.Reflection;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsTryParseBoundCultureTests
{
    // TryParseBound gates fromDate/toDate on an exact ISO yyyy-MM-dd parse and must do so
    // culture-independently. Under a non-Gregorian host culture (ar-SA Umm al-Qura) a
    // culture-sensitive parse would read "2024-03-15" as a Hijri year and reject the bound,
    // breaking every bounded fact query on such hosts. The invariant parse must recover the
    // Gregorian date. The existing arm tests don't pin culture. Oracle from the contract.
    [Fact]
    public void TryParseBound_IsoDateUnderUmmAlQuraCulture_ParsesGregorian()
    {
        var method = typeof(FinancialFactsTools).GetMethod(
            "TryParseBound",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var args = new object[] { "2024-03-15", null };
            var result = (bool)method!.Invoke(null, args);

            result.Should().BeTrue();
            ((DateOnly?)args[1]).Should().Be(new DateOnly(2024, 3, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
