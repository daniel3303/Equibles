using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperParseTransactionTypeSpacedAbbreviationTests
{
    // Contract (DisclosureParsingHelper.cs:255-257): "The House abbreviation may
    // carry a qualifier suffix ('S (partial)'), so accept the bare letter or the
    // letter followed by a space/'(' ...". Existing pins cover full words, the bare
    // letter ("S"), and the paren-no-space form ("S(full)") — but not the
    // space-separated qualifier, which is the doc's own example and reaches the
    // distinct StartsWith("S ") arm (no "Sale"/"Sold" word, not equal to "S").
    [Fact]
    public void ParseTransactionType_AbbreviationWithSpacedQualifier_ReturnsSale()
    {
        var result = DisclosureParsingHelper.ParseTransactionType("S (partial)");

        result.Should().Be(CongressTransactionType.Sale);
    }
}
