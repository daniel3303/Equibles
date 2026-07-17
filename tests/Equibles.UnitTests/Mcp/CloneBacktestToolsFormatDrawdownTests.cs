using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

/// <summary>
/// Regression pin for the clone backtest's drawdown cell (MCP audit 2026-07).
/// ComputeMaxDrawdown returns a positive LOSS magnitude; rendering it through the shared
/// signed total-return formatter produced "+20.2%", which reads as a gain and invites an LLM
/// to relay "max drawdown of +20.2%". The dedicated formatter renders it unsigned.
/// </summary>
public class CloneBacktestToolsFormatDrawdownTests
{
    private static readonly MethodInfo Method = typeof(CloneBacktestTools).GetMethod(
        "FormatDrawdown",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static string Format(decimal value) => (string)Method.Invoke(null, [value])!;

    [Fact]
    public void FormatDrawdown_PositiveLossMagnitude_RendersUnsigned()
    {
        Format(20.24m).Should().Be("20.2%");
    }

    [Fact]
    public void FormatDrawdown_Zero_RendersZero()
    {
        Format(0m).Should().Be("0.0%");
    }

    [Fact]
    public void FormatDrawdown_CultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Format(35.15m).Should().Be("35.2%");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
