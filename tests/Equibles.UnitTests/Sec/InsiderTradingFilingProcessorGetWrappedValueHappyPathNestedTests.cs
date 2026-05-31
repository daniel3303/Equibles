using System.Reflection;
using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorGetWrappedValueHappyPathNestedTests
{
    // Adversarial sibling to GetWrappedValue_PathMissingMidWalk_ReturnsNullWithoutThrowing.
    // The null-tolerance pin covers the failure path; nothing pins the
    // happy path — walk a nested path, then read the inner <value>.
    // The doc-comment is explicit: "Walk the path then read the inner
    // <value>." Two refactor risks slip past the null-path pin alone:
    //   (1) returning `element?.Value` instead of `element?.Element("value")?.Value`
    //       — concatenates ALL descendant text, so a sidecar element next
    //       to <value> contaminates the result.
    //   (2) walking only path[0] instead of every path[] entry — would
    //       still return null on the existing pin but read the wrong
    //       element when the first hop has a direct <value> child.
    //
    // The construction deliberately includes a SIDECAR sibling element
    // ("transactionPricePerShare") inside the inner wrapper. If a
    // refactor switched to `.Value` (concatenated descendant text), the
    // sidecar's text ("0.00") would bleed into the result. Asserting the
    // exact "1500" string catches both regression classes from one pin.
    [Fact]
    public void GetWrappedValue_NestedPathWithSidecarSibling_ReturnsInnerValueTextOnly()
    {
        var method = typeof(InsiderFilingParser).GetMethod(
            "GetWrappedValue",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // <nonDerivativeTransaction>
        //   <transactionAmounts>
        //     <transactionShares>
        //       <transactionPricePerShare>0.00</transactionPricePerShare>  ← sidecar
        //       <value>1500</value>
        //     </transactionShares>
        //   </transactionAmounts>
        // </nonDerivativeTransaction>
        var parent = new XElement(
            "nonDerivativeTransaction",
            new XElement(
                "transactionAmounts",
                new XElement(
                    "transactionShares",
                    new XElement("transactionPricePerShare", "0.00"),
                    new XElement("value", "1500")
                )
            )
        );
        var pathArg = new[] { "transactionAmounts", "transactionShares" };

        var result = (string)method!.Invoke(null, [parent, pathArg]);

        result
            .Should()
            .Be(
                "1500",
                "the contract is to walk the full path and read the inner <value>'s text only; a sidecar sibling must not bleed into the result"
            );
    }
}
