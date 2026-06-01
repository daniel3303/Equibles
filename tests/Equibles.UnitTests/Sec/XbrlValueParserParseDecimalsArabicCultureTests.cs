using System.Globalization;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class XbrlValueParserParseDecimalsArabicCultureTests
{
    // The XBRL @decimals attribute is an xs:integer (or "INF") per the XBRL 2.1 spec;
    // its value is an ASCII integer that must parse identically on any host. A negative
    // value carries meaning ("-6" = reported accurate to the nearest million), so the
    // parser must return -6 regardless of the host's CurrentCulture.
    //
    // ParseDecimals reaches the value through int.TryParse(string, out int) — the
    // overload that matches the leading sign against NumberFormatInfo.CurrentInfo.
    // NegativeSign rather than the ASCII hyphen. Under Arabic cultures (ar-SA's
    // NegativeSign is U+061C) the ASCII "-" no longer matches, so "-6" fails to parse
    // and the method returns null, silently dropping the rounding-scale metadata for a
    // valid filing. Every other numeric parse in the codebase pins
    // CultureInfo.InvariantCulture; this one does not.
    [Fact(
        Skip = "GH-3182 — ParseDecimals uses the culture-sensitive int.TryParse overload, so a negative @decimals returns null on non-invariant-culture hosts"
    )]
    public void ParseDecimals_NegativeValueUnderArabicCulture_PreservesTheInteger()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var result = XbrlValueParser.ParseDecimals("-6");

            result.Should().Be(-6);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
