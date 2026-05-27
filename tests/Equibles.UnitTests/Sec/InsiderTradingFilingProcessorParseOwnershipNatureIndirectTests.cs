using System.Reflection;
using System.Xml.Linq;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseOwnershipNatureIndirectTests
{
    // Sibling to InsiderTradingFilingProcessorParseOwnershipNatureAbsentTests.
    // That pin protects the absent-element fallback arm (→ Direct). This pin
    // covers the structurally distinct truthy arm: the explicit "I" value →
    // OwnershipNature.Indirect.
    //
    // The contract — `value == "I" ? Indirect : Direct` — encodes SEC Form 4's
    // primary ownership discriminator. Direct ownership means the insider
    // personally holds the security (shares registered in their name);
    // indirect ownership means it's held through a trust, spouse, partnership,
    // or similar pass-through. The two classifications drive completely
    // different insider-holdings analytics:
    //   • Direct holdings count toward the insider's personally-controlled
    //     exposure on the GetInsiderOwnership / GetTopHolders dashboards.
    //   • Indirect holdings are excluded from those personally-controlled
    //     queries but still surface in the GetInsiderTransactions feed
    //     (annotated as "through trust/spouse/etc").
    //
    // The risk this pin uniquely catches and that the Absent sibling cannot:
    //   • Swap regression — `value == "D" ? Indirect : Direct` (or the
    //     equivalent `value == "I" ? Direct : Indirect`) would compile,
    //     pass the Absent-element sibling (missing element still flows
    //     through to Direct — the fallback branch), and INVERT every
    //     Form 4 ownership classification across the entire SEC insider
    //     database. The insider-ownership concentration metric on the
    //     dashboard would invert: insiders who personally hold large
    //     blocks would appear as having "zero direct holdings" while
    //     every trust position would be misclassified as personally
    //     controlled — a material misrepresentation of insider
    //     concentration risk.
    //   • Drop regression — `return OwnershipNature.Direct;` (the
    //     "simplify away the rare indirect arm" refactor) would compile,
    //     pass the Absent-element sibling (Direct is the expected default
    //     there anyway), and silently classify every indirect holding —
    //     trust positions, spousal holdings, partnership interests — as
    //     direct. The legal-discovery implications matter: SEC §16(a)
    //     filings explicitly disclose indirect ownership precisely so the
    //     public can distinguish personal exposure from pass-through
    //     positions; collapsing the two undermines the disclosure
    //     framework the dashboard is meant to surface.
    //   • Case-sensitivity regression — `value?.ToUpperInvariant() == "I"`
    //     would compile and pass this pin (still returns Indirect for
    //     uppercase "I") but a lowercase-input "i" would surface as
    //     Direct under the original code (case-sensitive == comparison)
    //     vs Indirect under the case-insensitive refactor. Either way,
    //     pinning the uppercase "I" arm catches a regression that
    //     changes its return value.
    //
    // The pair (Absent → Direct + "I" → Indirect) defends both branches
    // of the ternary so any single-branch corruption fails on the
    // corresponding pin. A future iteration could add the explicit "D"
    // → Direct sibling to round out the case matrix.
    //
    // Construction: build a minimal transactionElement with the exact XML
    // shape ParseOwnershipNature walks — `<ownershipNature><directOrIndirectOwnership><value>I</value></...>`.
    // Asserting OwnershipNature.Indirect distinguishes:
    //   • Working "I" arm: returns Indirect.
    //   • Swap regression: returns Direct.
    //   • Drop regression: returns Direct.
    //   • Wrong-element-name regression (e.g. lookup walks the wrong path):
    //     returns Direct (fallback fires).
    [Fact]
    public void ParseOwnershipNature_OwnershipNatureValueI_ReturnsIndirect()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseOwnershipNature",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var transactionElement = new XElement(
            "nonDerivativeTransaction",
            new XElement(
                "ownershipNature",
                new XElement("directOrIndirectOwnership", new XElement("value", "I"))
            )
        );

        var result = (OwnershipNature)method!.Invoke(null, [transactionElement]);

        result.Should().Be(OwnershipNature.Indirect);
    }
}
