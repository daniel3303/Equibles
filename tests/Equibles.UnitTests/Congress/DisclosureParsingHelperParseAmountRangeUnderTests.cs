using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Adversarial: <see cref="DisclosureParsingHelper.ParseAmountRange"/> documents that a
/// single disclosed amount is exact unless its wording states an open bound. "Under $X" is an
/// UPPER bound, but a transacted security has a positive value, so it maps to (1, val). A future
/// change that lumps it into the open-top logic would silently flip the bounds.
/// </summary>
public class DisclosureParsingHelperParseAmountRangeUnderTests
{
    [Fact]
    public void ParseAmountRange_UnderPhrasing_IsPositiveUpperBound()
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("Under $50,000");

        from.Should().Be(1);
        to.Should().Be(50000);
    }
}
