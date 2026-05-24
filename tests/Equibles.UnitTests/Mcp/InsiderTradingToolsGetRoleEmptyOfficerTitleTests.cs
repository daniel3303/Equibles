using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class InsiderTradingToolsGetRoleEmptyOfficerTitleTests
{
    private static readonly MethodInfo GetRoleMethod = typeof(InsiderTradingTools).GetMethod(
        "GetRole",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // GetRole uses `owner.OfficerTitle ?? "Officer"` to fall back when the
    // title is null. But `??` does NOT fall through on empty string — an
    // officer with OfficerTitle == "" produces an empty token in the
    // comma-joined role list (e.g. "Director, " instead of
    // "Director, Officer"). A caller would reasonably expect a non-empty
    // label for every role flag that is set.
    [Fact]
    public void GetRole_OfficerWithEmptyTitle_FallsBackToOfficerLabel()
    {
        var owner = new InsiderOwner { IsOfficer = true, OfficerTitle = string.Empty };

        var role = (string)GetRoleMethod.Invoke(null, [owner]);

        role.Should().Be("Officer");
    }
}
