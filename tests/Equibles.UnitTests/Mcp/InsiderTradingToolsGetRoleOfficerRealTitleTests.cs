using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class InsiderTradingToolsGetRoleOfficerRealTitleTests
{
    // Contract (the Officer arm of GetRole's role-accumulation ladder):
    //   if (owner.IsOfficer)
    //       roles.Add(IsNullOrWhiteSpace(owner.OfficerTitle)
    //           ? "Officer"
    //           : owner.OfficerTitle);
    //
    // Existing sibling pins:
    //   • InsiderTradingToolsGetRoleEmptyOfficerTitleTests — Officer with
    //     EMPTY title → "Officer" fallback.
    //   • InsiderTradingToolsGetRoleWhitespaceOfficerTitleTests — Officer
    //     with WHITESPACE-ONLY title → "Officer" fallback.
    //   • InsiderTradingToolsGetRoleMultiRoleJoinTests — multi-role join
    //     using a NULL title (also falls back to "Officer").
    //   • InsiderTradingToolsGetRoleNoRoleFlagsTests — no roles → "Insider".
    //
    // ALL existing pins exercise the FALSE arm of the ternary (the
    // "Officer" fallback). The TRUE arm — using the actual `OfficerTitle`
    // verbatim when it's a real, non-blank string — is currently
    // UNPINNED. This is the path real production data takes: SEC Form 4
    // requires the OfficerTitle column for any officer (CFOs and CEOs
    // file with titles like "Chief Financial Officer", "Chief Executive
    // Officer", "President", "EVP, Global Operations"). The "Officer"
    // fallback only fires for legacy or malformed filings.
    //
    // The risks this pin uniquely catches and that are unreachable from
    // every existing sibling:
    //
    //   • Inverted ternary: a regression that swapped the two arms —
    //       `IsNullOrWhiteSpace(title) ? title : "Officer"`
    //     — would compile cleanly, pass every existing pin (the empty/
    //     whitespace inputs still return "Officer" because the inverted
    //     ternary's FALSE arm now hardcodes that literal), and silently
    //     render "Officer" for EVERY CFO/CEO/President in the system.
    //     The MCP `SearchInsiders` tool would emit "Officer" for every
    //     officer row instead of "Chief Financial Officer" — the LLM
    //     consuming it would lose the title context that drives most
    //     downstream insider-attribution analytics.
    //
    //   • Dropped title substitution: a "simplify" refactor that removed
    //     the ternary and always used "Officer" — under the (false)
    //     intuition that "we have a dedicated OfficerTitle column on
    //     InsiderOwner; the MCP tool should just point users at it" —
    //     would compile, pass every existing pin (all return "Officer"),
    //     and silently flatten every officer row to the generic label.
    //
    //   • Switched property: a copy-paste edit that pulled from a sibling
    //     property — e.g. `owner.PersonName ?? "Officer"` — would compile
    //     if the swapped property exists on InsiderOwner. Existing pins
    //     don't read OfficerTitle's value (their inputs trip the
    //     fallback), so the wrong-property regression slips past every
    //     existing assertion.
    //
    // Production analog: "Chief Financial Officer" is the modal officer
    // title in the SEC Form 4 corpus (every issuer files a Form 4 for
    // their CFO at any executive-compensation grant or open-market trade).
    // Misrendering it as "Officer" affects the highest-volume officer
    // role in the entire insider-trading dataset.
    //
    // Pin: invoke GetRole with `IsOfficer=true, OfficerTitle="Chief
    // Financial Officer"`, no other role flags. Assert the result is
    // exactly "Chief Financial Officer" — proves the true arm of the
    // ternary fires AND the verbatim title flows through `roles.Add`
    // to the final `string.Join` output. The exact-string equality
    // catches both the inversion (returns "Officer") and the drop
    // (returns "Officer").
    [Fact]
    public void GetRole_OfficerWithRealTitle_UsesTitleVerbatimNotOfficerFallback()
    {
        var method = typeof(InsiderTradingTools).GetMethod(
            "GetRole",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var owner = new InsiderOwner
        {
            IsDirector = false,
            IsOfficer = true,
            OfficerTitle = "Chief Financial Officer",
            IsTenPercentOwner = false,
        };

        var role = (string)method!.Invoke(null, [owner]);

        role.Should().Be("Chief Financial Officer");
    }
}
