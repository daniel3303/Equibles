using System.Reflection;
using Equibles.Finra.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

// GetShortInterest may append a pending-settlement estimate supplied by the deployment. The table
// above it is FINRA's reported record, so the two rules that keep the estimate from being read as
// reported data — it is separated by a BLANK line, and it only appears at all when the caller's
// window runs to the present — are pinned here rather than left to the call site's formatting.
public class ShortDataToolsEstimateAppendTests
{
    private static string AppendEstimate(string answer, string estimate) =>
        (string)
            typeof(ShortDataTools)
                .GetMethod("AppendEstimate", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [answer, estimate]);

    private static bool WindowReachesPresent(DateOnly end) =>
        (bool)
            typeof(ShortDataTools)
                .GetMethod("WindowReachesPresent", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [end]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoEstimateLeavesTheAnswerExactlyAsItWas(string estimate)
    {
        const string answer = "Short interest for NVDA (Nvidia Corp):\n\n| a |\n|---|\n| 1 |\n";

        AppendEstimate(answer, estimate)
            .Should()
            .BeSameAs(
                answer,
                "a deployment that registers no estimate source must get byte-identical output"
            );
    }

    [Fact]
    public void TheEstimateIsSeparatedFromTheTableByABlankLine()
    {
        var result = AppendEstimate(
            "Short interest for NVDA (Nvidia Corp):\n\n| a |\n|---|\n| 1 |\n",
            "**Model estimate — not official FINRA data.**"
        );

        result
            .Should()
            .Contain(
                "| 1 |\n\n**Model estimate",
                "a single newline makes the block a lazy continuation of the table's last line in CommonMark, which is exactly the merge the separation exists to prevent"
            );
    }

    // The no-rows answer is a bare sentence with no trailing newline. Without normalising the
    // join, the estimate fused into "No short interest data found for X ..." and the tool's only
    // content became a prediction wearing a no-data sentence as its opening clause.
    [Fact]
    public void AnEmptyResultDoesNotSwallowTheEstimateIntoItsSentence()
    {
        var result = AppendEstimate(
            "No short interest data found for NVDA in the specified date range.",
            "**Model estimate — not official FINRA data.**"
        );

        result
            .Should()
            .Be(
                "No short interest data found for NVDA in the specified date range.\n\n**Model estimate — not official FINRA data.**\n"
            );
    }

    [Fact]
    public void ADefaultWindowRunningToTodayCarriesTheEstimate()
    {
        WindowReachesPresent(DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeTrue();
        WindowReachesPresent(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30))
            .Should()
            .BeTrue("a window ending in the future still asks about the present");
    }

    [Fact]
    public void AHistoricalWindowDoesNotCarryATodayEstimate()
    {
        WindowReachesPresent(new DateOnly(2019, 12, 31))
            .Should()
            .BeFalse(
                "appending a pending-settlement estimate under a 2019 table answers a question the caller did not ask"
            );
        WindowReachesPresent(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)).Should().BeFalse();
    }
}
