using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class InsiderTradingToolsGetRoleWhitespaceOfficerTitleTests
{
    private static readonly MethodInfo GetRoleMethod = typeof(InsiderTradingTools).GetMethod(
        "GetRole",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // string.IsNullOrEmpty does not catch whitespace-only strings.
    // A whitespace-only OfficerTitle should fall back to "Officer"
    // just like null and empty do — it's not a meaningful label.
    [Fact(
        Skip = "GH-2014 — GetRole returns whitespace for officer with whitespace-only OfficerTitle"
    )]
    public void GetRole_OfficerWithWhitespaceTitle_FallsBackToOfficerLabel()
    {
        var owner = new InsiderOwner { IsOfficer = true, OfficerTitle = "   " };

        var role = (string)GetRoleMethod.Invoke(null, [owner]);

        role.Should().Be("Officer");
    }
}
