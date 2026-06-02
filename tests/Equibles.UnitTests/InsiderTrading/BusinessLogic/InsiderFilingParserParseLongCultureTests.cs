using System.Globalization;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserParseLongCultureTests
{
    // Adversarial: ParseLong first tries the culture-sensitive long.TryParse(string, out long)
    // overload, which matches a leading sign against CurrentCulture.NegativeSign, not the ASCII
    // hyphen — the same fork class as GH-3182 (XbrlValueParser). The SEC ownership XML carries
    // ASCII signed integers, and a negative share count is a real input (malformed/amended Form 4,
    // already pinned in the validator's negative-shares test). The contract: a share count parses
    // identically on any host culture. Here ar-SA (NegativeSign = U+061C) is the canonical
    // non-hyphen culture; ParseLong must still recover -1000 via its invariant ParseDecimal
    // fallback, never silently collapse to 0.
    [Fact]
    public void ParseLong_NegativeIntegerUnderArabicCulture_PreservesTheValue()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            InsiderFilingParser
                .ParseLong("-1000")
                .Should()
                .Be(
                    -1000,
                    "a Form 4 share count is ASCII and locale-independent; a non-hyphen "
                        + "NegativeSign must not fork the parse (cf. GH-3182)"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
