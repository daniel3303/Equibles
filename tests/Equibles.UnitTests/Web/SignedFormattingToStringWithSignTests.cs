using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class SignedFormattingToStringWithSignTests
{
    // Contract: a leading "+" is added only for positive values; negatives keep
    // the format's own "-" (never "+-"), and zero is unsigned. Format "0" avoids
    // grouping separators so the assertion is culture-independent.
    [Fact]
    public void ToStringWithSign_PrefixesPositiveOnly_NegativeAndZeroUnprefixed()
    {
        5.ToStringWithSign("0").Should().Be("+5");
        (-5).ToStringWithSign("0").Should().Be("-5");
        0.ToStringWithSign("0").Should().Be("0");
    }
}
