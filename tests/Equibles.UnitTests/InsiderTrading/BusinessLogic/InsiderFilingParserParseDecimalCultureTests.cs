using System.Globalization;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserParseDecimalCultureTests
{
    // SEC Form 4 amounts (price per share, value) carry an ASCII '.' decimal point. ParseDecimal
    // must read them culture-independently: under de-DE — where '.' is the THOUSANDS separator — a
    // culture-sensitive parse of "12.50" yields 1250, off by 100x. The invariant parse must recover
    // 12.50m. Pins the InvariantCulture guard against a locale fork. Oracle from the contract.
    [Fact]
    public void ParseDecimal_DecimalPointUnderGermanCulture_ParsesAsInvariantNotThousands()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            InsiderFilingParser.ParseDecimal("12.50").Should().Be(12.50m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
