using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class InvestmentAdviserToolsFormatFeeStructureMultipleFlagsTests
{
    // FormatFeeStructure renders the "Fee structure" line of the GetInvestmentAdviser
    // MCP profile from the seven Form ADV Item 5.E compensation flags. Contract: emit
    // one label per *set* flag, comma-joined, in the fixed Item 5.E(1)→(7) order
    // (percentage of AUM, hourly, subscription, fixed fees, commissions,
    // performance-based, other), and omit every unset flag. The existing suite only
    // pins the all-flags-false placeholder ("-") and the lone percentage-of-AUM case,
    // so the six non-percentage labels and the multi-flag join/order are unexercised.
    //
    // Pin a mixed selection — hourly + fixed + performance-based set, the rest false —
    // which uniquely guards: (a) the source-defined ordering survives a refactor, and
    // (b) unset flags (percentage, subscription, commissions, other) are excluded
    // rather than always-listed. Reflection-invoke since the helper is private static.
    [Fact]
    public void FormatFeeStructure_MixedFlags_JoinsSetLabelsInItem5EOrder()
    {
        var adviser = new FormAdvAdviser
        {
            ChargesHourly = true,
            ChargesFixed = true,
            ChargesPerformanceBased = true,
            // ChargesPercentageOfAum, ChargesSubscription, ChargesCommissions,
            // ChargesOther left false — must not appear in the rendered list.
        };

        var method = typeof(InvestmentAdviserTools).GetMethod(
            "FormatFeeStructure",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [adviser]);

        result.Should().Be("hourly, fixed fees, performance-based");
    }
}
