using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserGetWrappedValueMissingPathTests
{
    // GetWrappedValue walks a path of nested elements then reads the inner <value>. Every Form 4
    // field read goes through it, so an absent intermediate element must yield null gracefully —
    // never an NRE. Here the parent has "ownershipNature" but not the "directOrIndirectOwnership"
    // leaf, so the mid-walk null-propagation must short-circuit to null. Oracle from the contract.
    [Fact]
    public void GetWrappedValue_IntermediatePathElementMissing_ReturnsNullWithoutThrowing()
    {
        var element = new XElement("transactionEntry", new XElement("ownershipNature"));

        var result = InsiderFilingParser.GetWrappedValue(
            element,
            "ownershipNature",
            "directOrIndirectOwnership"
        );

        result.Should().BeNull();
    }
}
