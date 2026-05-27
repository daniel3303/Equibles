using System.Globalization;
using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientParseDecimalCultureTests
{
    // Sibling to CftcClientParseDecimalNullTests (null guard) and the ParseInt
    // thousands-separator pin. ParseDecimal is the only one of the three
    // numeric helpers that does NOT strip commas, so the dot in "12.5" is the
    // sole decimal indicator the parser ever sees. The body passes
    // CultureInfo.InvariantCulture explicitly — without it the host culture's
    // NumberFormatInfo decides whether "." is a decimal point or a thousands
    // separator. On a comma-decimal host (pt-PT, fr-FR, de-DE) a refactor to
    // `decimal.TryParse(value, out result)` (a tempting "drop the redundant
    // culture arg" cleanup) would parse "12.5" as 125m and silently triple
    // every percentage-of-OI cell in the weekly COT import. Pin the invariant.
    [Fact]
    public void ParseDecimal_DotDecimalUnderCommaDecimalCulture_ParsesAsInvariant()
    {
        var method = typeof(CftcClient).GetMethod(
            "ParseDecimal",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-PT");

            var result = (decimal?)method.Invoke(null, ["12.5"]);

            result.Should().Be(12.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
