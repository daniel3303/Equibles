using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class CloneBacktestToolsFormatCagrNullWindowTests
{
    // BacktestStrategySummary.CagrPercent is null when the simulated window is shorter than the
    // calculator's minimum annualization span — annualizing a few weeks of return would explode
    // into a meaningless multi-thousand-percent CAGR (the same trap that produced the manager-
    // performance leaderboard's +96,699% rows). FormatCagr's null arm must render the literal
    // em-dash placeholder — never "0.0%" (which reads as a real flat result) and never throw.
    // A refactor that unwrapped value.Value unconditionally would NullReference on every
    // too-short clone window; this pins the null arm to the exact "—".
    [Fact]
    public void FormatCagr_NullCagr_RendersEmDashPlaceholder()
    {
        var method = typeof(CloneBacktestTools).GetMethod(
            "FormatCagr",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [(decimal?)null]);

        result.Should().Be("—");
    }
}
