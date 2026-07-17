using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Regression pin for the "-+0.0%" glitch in GetOwnershipHistory's Change column (MCP audit
/// 2026-07). The old two-section format "+0.0;-0.0" let a negative change that rounds to zero
/// re-format through the POSITIVE section while the negative-zero double kept its sign,
/// emitting the garbled "-+0.0" — and the combined current-quarter row's carried-forward
/// share change is near-zero by construction, so the glitch showed on almost every call while
/// a 13F filing window was open. The three-section format routes a rounds-to-zero value
/// through the zero section instead.
/// </summary>
public class InstitutionalHoldingsToolsFormatShareChangeNegativeZeroTests
{
    private static readonly MethodInfo Method = typeof(InstitutionalHoldingsTools).GetMethod(
        "FormatShareChange",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static string Format(long totalShares, long previousShares) =>
        (string)Method.Invoke(null, [totalShares, previousShares])!;

    [Fact]
    public void FormatShareChange_NegativeChangeThatRoundsToZero_RendersPlainZero()
    {
        // -0.0046% rounds to zero at one decimal — must render "0.0%", never "-+0.0%".
        var rendered = Format(totalShares: 999_954, previousShares: 1_000_000);

        rendered.Should().Be("0.0%");
    }

    [Fact]
    public void FormatShareChange_RealChanges_KeepExplicitSigns()
    {
        Format(totalShares: 1_500_000, previousShares: 1_000_000).Should().Be("+50.0%");
        Format(totalShares: 949_000, previousShares: 1_000_000).Should().Be("-5.1%");
    }

    [Fact]
    public void FormatShareChange_NoPriorShares_RendersDash()
    {
        Format(totalShares: 1_000, previousShares: 0).Should().Be("—");
    }

    [Fact]
    public void FormatShareChange_CultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Format(totalShares: 1_500_000, previousShares: 1_000_000).Should().Be("+50.0%");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
