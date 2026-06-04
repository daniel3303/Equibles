using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperParseAmountRangeThreeAmountsTests
{
    // Contract: a disclosed amount bracket has two bounds — from and to. When the
    // cell carries a third dollar figure (e.g. a trailing parenthetical fee note),
    // the bounds are still the first two amounts; the extra figure is not a bound.
    // Existing pins only exercise exactly-two amounts, leaving the `>= 2` (not
    // `== 2`) branch untested — a regression reading the last two would silently
    // shift the reported range.
    [Fact]
    public void ParseAmountRange_ThreeDollarAmounts_UsesFirstTwoAsBounds()
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange(
            "$1,001 - $15,000 (incl. $50 fee)"
        );

        from.Should().Be(1_001);
        to.Should().Be(15_000);
    }
}
