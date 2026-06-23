using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Adversarial: <see cref="DisclosureParsingHelper.ParseAmountRange"/> documents that a
/// single disclosed amount is an open-ended LOWER bound only when phrased "Over $X" or as
/// the House top bracket "$X +"; any other single amount — e.g. "Under $X" — is an UPPER
/// bound and must map to (0, val). The "Under" phrasing is otherwise untested, so a future
/// change that lumps it into the open-top logic would silently flip the bounds.
/// </summary>
public class DisclosureParsingHelperParseAmountRangeUnderTests
{
    [Fact]
    public void ParseAmountRange_UnderPhrasing_IsUpperBoundFromZero()
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("Under $50,000");

        from.Should().Be(0);
        to.Should().Be(50000);
    }
}
