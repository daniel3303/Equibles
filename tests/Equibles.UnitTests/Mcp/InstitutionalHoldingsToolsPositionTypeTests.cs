using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class InstitutionalHoldingsToolsPositionTypeTests
{
    private static string Describe(OptionType? optionType, ShareType shareType = ShareType.Shares)
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "PositionType",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull("the portfolio tables label each line through this helper");
        return (string)method.Invoke(null, [optionType, shareType]);
    }

    [Fact]
    public void PositionType_PutLeg_SaysPut()
    {
        // The line that made this necessary: Scion's Q3 2025 filing reports 5,000,000 Palantir
        // shares at 67% of the portfolio, and it is a PUT — Burry was betting against Palantir.
        // Rendered without the label it reads as the largest long position in the book, which
        // reverses the manager's actual view. OptionType.Put is the enum's zero value, so a
        // switch that falls through to a default lands precisely on the most misleading case.
        Describe(OptionType.Put).Should().Be("Put");
    }

    [Fact]
    public void PositionType_CallLeg_SaysCall()
    {
        Describe(OptionType.Call).Should().Be("Call");
    }

    [Fact]
    public void PositionType_NoOptionType_SaysCommon()
    {
        // Shares held outright. Naming it rather than leaving the cell blank matters: a blank
        // reads as missing data, and the whole point of the column is that every line states what
        // it is.
        Describe(null).Should().Be("Common");
    }

    [Fact]
    public void PositionType_PrincipalRow_SaysPrincipal()
    {
        Describe(null, ShareType.Principal).Should().Be("Principal");
    }
}
