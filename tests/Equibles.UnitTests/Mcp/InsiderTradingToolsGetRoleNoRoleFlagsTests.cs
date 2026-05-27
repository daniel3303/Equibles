using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class InsiderTradingToolsGetRoleNoRoleFlagsTests
{
    // Sibling to the existing empty/whitespace OfficerTitle pins. Those defend
    // the in-list-empty-token case. This pin covers the structurally distinct
    // EMPTY-ROLES default arm — the only path that returns the literal
    // "Insider" instead of joining the accumulated list:
    //   return roles.Count > 0 ? string.Join(", ", roles) : "Insider";
    //
    // The risk this pin uniquely catches:
    //   • A refactor that "tidies the ternary" to `string.Join(", ", roles)`
    //     unconditionally — under the (false) intuition that an empty role
    //     list naturally produces an empty string anyway — would compile,
    //     pass every existing test (each sets at least one role flag), and
    //     silently change the displayed label for every Form 4 owner whose
    //     IsDirector / IsOfficer / IsTenPercentOwner all happen to be false.
    //     Real production occurrence: post-correction amendments occasionally
    //     strip role flags before the new flags are re-applied; the
    //     half-stitched amendment row briefly lacks flags and renders in
    //     the insider-transactions feed. With the fallback in place, that
    //     row displays as "Insider" (a sensible best-guess label); without
    //     it, an empty cell shows up in the MCP-tool response and the LLM
    //     consuming it produces sentence fragments like "filed by .". The
    //     existing OfficerTitle pins can't see this — both set IsOfficer=true,
    //     so roles is non-empty in those tests.
    //
    //   • A swap regression — `: "Unknown"` or `: ""` — would compile and
    //     change the user-visible default. Asserting the EXACT "Insider"
    //     literal distinguishes the working default from any alternative.
    //
    // Construction: an InsiderOwner with ALL role flags false. No element
    // is appended to `roles` so `roles.Count == 0` and the ternary's false
    // arm fires. The exact literal "Insider" must be returned.
    [Fact]
    public void GetRole_NoRoleFlagsSet_ReturnsLiteralInsiderFallback()
    {
        var method = typeof(InsiderTradingTools).GetMethod(
            "GetRole",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var owner = new InsiderOwner
        {
            IsDirector = false,
            IsOfficer = false,
            IsTenPercentOwner = false,
        };

        var role = (string)method!.Invoke(null, [owner]);

        role.Should().Be("Insider");
    }
}
