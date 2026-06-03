using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperParseOptionTypeMixedCaseTests
{
    [Fact]
    public void ParseOptionType_SecSchemaMixedCaseCall_ReturnsCallEnum()
    {
        // The existing PUT/CALL pins use all-caps, but the SEC 13F information-table
        // <putCall> element actually emits mixed-case "Put"/"Call" on the wire. The
        // ToUpperInvariant normalization exists precisely to accept that real casing;
        // a refactor that dropped it (assuming uppercase input) would pass every
        // all-caps test yet misclassify every live option holding as null.
        var result = HoldingsParsingHelper.ParseOptionType("Call");

        result.Should().Be(OptionType.Call);
    }
}
