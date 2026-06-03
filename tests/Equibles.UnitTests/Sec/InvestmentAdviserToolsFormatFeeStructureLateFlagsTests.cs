using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class InvestmentAdviserToolsFormatFeeStructureLateFlagsTests
{
    // Complements the MixedFlags pin (hourly + fixed + performance-based). FormatFeeStructure
    // emits one label per set Form ADV Item 5.E flag, comma-joined, in fixed 5.E(1)→(7) order.
    // The three later flags — subscription 5.E(3), commissions 5.E(5), other 5.E(7) — have their
    // set-arm unexercised; this pins their exact labels and that order survives between them.
    [Fact]
    public void FormatFeeStructure_SubscriptionCommissionsOther_JoinsInItem5EOrder()
    {
        var adviser = new FormAdvAdviser
        {
            ChargesSubscription = true,
            ChargesCommissions = true,
            ChargesOther = true,
            // ChargesPercentageOfAum, ChargesHourly, ChargesFixed, ChargesPerformanceBased
            // left false — must not appear in the rendered list.
        };

        var method = typeof(InvestmentAdviserTools).GetMethod(
            "FormatFeeStructure",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [adviser]);

        result.Should().Be("subscription, commissions, other");
    }
}
