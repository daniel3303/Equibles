using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsFormatPercentCultureInvarianceTests
{
    // Adversarial Lane A. FormatPercent (extracted in #2716) documents one
    // decimal place in INVARIANT culture so the separator stays stable across
    // host locales — the same bug class repeatedly fixed elsewhere (FormatSigned*
    // / consensus-cell). A regression dropping CultureInfo.InvariantCulture would
    // render "12,3" under de-DE. Contract derived from the doc-comment, not the body.
    [Fact]
    public void FormatPercent_UnderNonInvariantCulture_UsesInvariantDecimalSeparator()
    {
        var method = typeof(InstitutionalHoldingsTools)
            .GetMethod("FormatPercent", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(double));

        var previous = CultureInfo.CurrentCulture;
        try
        {
            // de-DE uses a comma decimal separator — the canonical non-invariant locale.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = (string)method.Invoke(null, [12.34])!;
            result.Should().Be("12.3");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
