using System.Globalization;
using Equibles.CommonStocks.Data.Helpers;

namespace Equibles.UnitTests.CommonStocks;

public class TickerNormalizerNormalizeCultureInvarianceTests
{
    // Contract: Normalize is the canonical ticker form for case-insensitive lookups and
    // upper-cases with the INVARIANT culture so a host's locale can't fork the mapping.
    // Under tr-TR a culture-sensitive ToUpper maps lowercase 'i' to the dotted capital 'İ'
    // (U+0130); the canonical form of "visa" must stay ASCII "VISA" on every host, or the
    // same ticker keys to two different strings depending on where the process runs.
    [Fact]
    public void Normalize_LowercaseIUnderTurkishCulture_UppercasesToAsciiInvariantly()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var result = TickerNormalizer.Normalize("visa");

            result.Should().Be("VISA");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
