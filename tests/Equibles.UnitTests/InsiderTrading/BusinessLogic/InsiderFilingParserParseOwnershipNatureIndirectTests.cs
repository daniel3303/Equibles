using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserParseOwnershipNatureIndirectTests
{
    // SEC Form 4 ownershipNature/directOrIndirectOwnership uses "D" for direct and "I" for
    // indirect holdings. A holding flagged "I" must classify as Indirect — mislabelling it
    // Direct would corrupt insider-ownership analysis. Pins the non-default arm and the wrapped
    // <field><value> extraction. Oracle from the SEC convention, not the body.
    [Fact]
    public void ParseOwnershipNature_DirectOrIndirectValueI_ReturnsIndirect()
    {
        var element = new XElement(
            "transactionEntry",
            new XElement(
                "ownershipNature",
                new XElement("directOrIndirectOwnership", new XElement("value", "I"))
            )
        );

        var result = InsiderFilingParser.ParseOwnershipNature(element);

        result.Should().Be(OwnershipNature.Indirect);
    }
}
