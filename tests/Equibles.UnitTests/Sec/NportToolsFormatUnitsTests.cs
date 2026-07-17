using System.Reflection;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the Form NPORT-P unit-of-measure decoding on GetFundsHoldingStock (MCP audit
/// 2026-07). The raw Item C.7 codes ("NS", "PA", "NC", "OU") are opaque to an MCP consumer
/// exactly when the Balance column's meaning matters most (bonds and derivatives report par
/// values and contract counts, not shares); unknown codes pass through as filed so a new SEC
/// code never renders as a wrong label.
/// </summary>
public class NportToolsFormatUnitsTests
{
    private static readonly MethodInfo Method = typeof(NportTools).GetMethod(
        "FormatUnits",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static string Format(string units) => (string)Method.Invoke(null, [units])!;

    [Theory]
    [InlineData("NS", "Shares")]
    [InlineData("PA", "Principal (par)")]
    [InlineData("NC", "Contracts")]
    [InlineData("OU", "Other")]
    public void FormatUnits_KnownNportCode_DecodesToHumanLabel(string code, string expected)
    {
        Format(code).Should().Be(expected);
    }

    [Fact]
    public void FormatUnits_UnknownCode_PassesThroughAsFiled()
    {
        Format("ZZ").Should().Be("ZZ");
    }

    [Fact]
    public void FormatUnits_Null_RendersDash()
    {
        Format(null).Should().Be("-");
    }
}
