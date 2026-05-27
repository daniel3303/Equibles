using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperParseShareTypeNullTests
{
    [Fact]
    public void ParseShareType_NullInput_ReturnsSharesWithoutThrowing()
    {
        // Sibling to the just-added ParseInvestmentDiscretion null pin and
        // the existing ParseShareType siblings (PRN, SH, unrecognized).
        // ParseShareType (HoldingsParsingHelper.cs:67) is `value?.ToUpperInvariant()
        // switch { "SH" => Shares, "PRN" => Principal, _ => Shares }`. The `?.`
        // null-conditional turns null into a null pattern that matches the
        // default and yields Shares. A refactor dropping the `?.` (under
        // "the upstream always provides a value") would NRE on the first 13F
        // row with a missing share-type cell — taking down the quarterly
        // import. SH/PRN/unrecognized cover non-null arms; null is unpinned.
        var result = HoldingsParsingHelper.ParseShareType(null);

        result.Should().Be(ShareType.Shares);
    }
}
