using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class InsiderTradingToolsGetRoleMultiRoleJoinTests
{
    [Fact]
    public void GetRole_AllThreeRoleFlagsSet_JoinsWithCommaAndPreservesOrder()
    {
        // Sibling to the WhitespaceOfficerTitle / EmptyOfficerTitle / NoRoleFlags
        // pins. Those defend single-role and zero-role paths; the multi-role
        // join is unpinned. Form 4 routinely lists insiders with two or three
        // roles (a CEO who is also a director, or a founder who's all three).
        // The contract:
        //   • Order matches the if-chain: Director, Officer, 10% Owner
        //   • Separator is exactly ", " (comma + space)
        //   • Officer entry uses "Officer" when title is blank
        // A refactor that swaps string.Join(", ") for string.Concat (no
        // separator) would compile, pass every existing single-role pin, and
        // render "DirectorOfficer10% Owner" to the LLM — unparseable garbage.
        // A reorder of the if-chain would silently change the canonical
        // labelling on every multi-role owner.
        var method = typeof(InsiderTradingTools).GetMethod(
            "GetRole",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var owner = new InsiderOwner
        {
            IsDirector = true,
            IsOfficer = true,
            OfficerTitle = null,
            IsTenPercentOwner = true,
        };

        var role = (string)method!.Invoke(null, [owner]);

        role.Should().Be("Director, Officer, 10% Owner");
    }
}
