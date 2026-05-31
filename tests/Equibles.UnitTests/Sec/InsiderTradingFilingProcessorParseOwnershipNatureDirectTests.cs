using System.Reflection;
using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseOwnershipNatureDirectTests
{
    // Third sibling in the ParseOwnershipNature family (after Absent and
    // Indirect). The existing siblings cover:
    //   • Absent element → Direct (fallback arm of the ternary)
    //   • Explicit "I" value → Indirect (truthy arm)
    // The Indirect sibling's comment explicitly flags this gap: "A future
    // iteration could add the explicit 'D' → Direct sibling to round out
    // the case matrix."
    //
    // The risk this pin uniquely catches and that the other two siblings
    // cannot:
    //   • Case-sensitivity regression — `value?.ToUpperInvariant() == "I"`.
    //     The Indirect sibling passes (uppercase "I" still maps to
    //     Indirect). The Absent sibling passes (no value at all → Direct).
    //     But lowercase "d" or any non-"I" value would still flow through
    //     the case-insensitive ToUpperInvariant comparison... hmm, that's
    //     not quite right. Let me restate:
    //   • The Absent sibling fires the fallback because the element is
    //     missing. The Direct case here fires the fallback because the
    //     value is explicitly "D" and the comparison `"D" == "I"` is
    //     false. These are STRUCTURALLY DISTINCT — a regression that
    //     skipped the equality check entirely (`return Direct;` for any
    //     present element) would pass both Absent and Direct but
    //     break Indirect. A regression that always returned Indirect
    //     when the element is present (`return present ? Indirect :
    //     Direct;`) would pass Absent and Indirect but break this Direct
    //     pin. The three-pin matrix closes both gaps.
    //
    // Construction: minimal transactionElement carrying
    // `<ownershipNature><directOrIndirectOwnership><value>D</value></...>`
    // — the SEC Form 4 standard encoding for direct ownership.
    [Fact]
    public void ParseOwnershipNature_OwnershipNatureValueD_ReturnsDirect()
    {
        var method = typeof(InsiderFilingParser).GetMethod(
            "ParseOwnershipNature",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var transactionElement = new XElement(
            "nonDerivativeTransaction",
            new XElement(
                "ownershipNature",
                new XElement("directOrIndirectOwnership", new XElement("value", "D"))
            )
        );

        var result = (OwnershipNature)method!.Invoke(null, [transactionElement]);

        result.Should().Be(OwnershipNature.Direct);
    }
}
