using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsFormatSignedMillionsCultureInvarianceTests
{
    private static readonly MethodInfo FormatSignedMillionsMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "FormatSignedMillions",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static string Invoke(decimal value) =>
        (string)FormatSignedMillionsMethod.Invoke(null, [value]);

    // FormatSignedMillions is the shared Δ Value ($M) formatter feeding the
    // delta-value cells across GetInstitutionQuarterlyActivity, GetTopBuyersSellers,
    // GetMarketWide13FActivity and GetMostHeldStocks. It applies the custom format
    // "+#,##0.0;-#,##0.0;0.0" with no IFormatProvider, so it follows CurrentCulture.
    // Its sibling FormatMillions threads InvariantCulture and every other numeric
    // cell in this file is invariant, so the contract is byte-identical output
    // regardless of host CurrentCulture. This attacks the culture risk surface under de-DE.
    [Fact]
    public void FormatSignedMillions_UnderNonInvariantCulture_RendersInvariantSeparators()
    {
        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = Invoke(1_234_500_000m);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = Invoke(1_234_500_000m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        invariantOutput.Should().Be("+1,234.5");
        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown is consumed by LLMs trained on en-US conventions; FormatSignedMillions omits the IFormatProvider, so it follows CurrentCulture (de-DE swaps the group/decimal separators to +1.234,5), forking the response by host locale — same bug class as the FormatMillions invariant sibling"
            );
    }
}
