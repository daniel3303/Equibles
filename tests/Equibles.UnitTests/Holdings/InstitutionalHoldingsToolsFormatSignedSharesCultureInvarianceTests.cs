using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsFormatSignedSharesCultureInvarianceTests
{
    private static readonly MethodInfo FormatSignedSharesMethod = typeof(InstitutionalHoldingsTools)
        .GetMethod("FormatSignedShares", BindingFlags.NonPublic | BindingFlags.Static)
        .MakeGenericMethod(typeof(long));

    private static string Invoke(long value) =>
        (string)FormatSignedSharesMethod.Invoke(null, [value]);

    // FormatSignedShares is the shared signed-share-count formatter feeding the
    // Δ Shares cells across the 13F activity tools. Its sibling FormatMillions
    // explicitly threads InvariantCulture and every other numeric cell in this
    // file is invariant, so the contract is that a share count renders
    // byte-identically regardless of host CurrentCulture (en-US grouping). This
    // attacks the culture/encoding risk surface under de-DE.
    [Fact(Skip = "GH-2675 — FormatSignedShares passes null IFormatProvider; share counts misformat under de-DE")]
    public void FormatSignedShares_UnderNonInvariantCulture_RendersInvariantGrouping()
    {
        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = Invoke(1_234_567L);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = Invoke(1_234_567L);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        invariantOutput.Should().Be("+1,234,567");
        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown is consumed by LLMs trained on en-US conventions; FormatSignedShares passes null as the IFormatProvider, so it follows CurrentCulture (de-DE swaps the grouping separator to '.'), forking the response by host locale — same bug class as the FormatMillions invariant sibling"
            );
    }
}
