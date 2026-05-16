using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperPartialSaleTests
{
    // Contract (documented at DisclosureParsingHelper.cs, ParseTransactionType
    // comment): "Handles both Senate full words ("Purchase", "Sale (Full)") and
    // House abbreviations ("P", "S", "S (partial)")". "S (partial)" is an
    // explicitly listed House abbreviation, so per the contract it must resolve
    // to Sale. Existing tests pin "Sale (Partial)" and bare "S" but not this
    // exact documented token.
    [Fact]
    public void ParseTransactionType_HouseAbbreviationSPartial_ReturnsSale()
    {
        var result = DisclosureParsingHelper.ParseTransactionType("S (partial)");

        result.Should().Be(CongressTransactionType.Sale);
    }
}
