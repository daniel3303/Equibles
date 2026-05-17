using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperParseAmountRangeOpenTopTests
{
    // Contract: ParseAmountRange maps a disclosed amount bracket to (from, to).
    // The House top bracket "$50,000,000 +" is a lower-bounded open range
    // (>= $50M) — semantically identical to "Over $50,000,000", which the
    // existing pin maps to (val, val). So `from` must be 50,000,000, not 0;
    // returning (0, 50M) inverts the position (claims it is AT MOST $50M).
    [Fact(Skip = "GH-780 — ParseAmountRange mishandles '$X +' open-top bracket")]
    public void ParseAmountRange_PlusSuffixOpenTopBracket_LowerBoundIsTheValue()
    {
        var (from, _) = DisclosureParsingHelper.ParseAmountRange("$50,000,000 +");

        from.Should().Be(50_000_000);
    }
}
