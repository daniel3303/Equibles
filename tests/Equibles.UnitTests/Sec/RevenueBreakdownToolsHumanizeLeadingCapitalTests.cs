using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The PascalCase-splitting regex used to break a lone leading capital off the
/// word that follows, mangling Apple's flagship member labels: IPhoneMember →
/// "I Phone", IPadMember → "I Pad". A lone leading capital stays attached to
/// its word; boundaries deeper in the name must still split.
/// </summary>
public class RevenueBreakdownToolsHumanizeLeadingCapitalTests
{
    [Theory]
    [InlineData("aapl:IPhoneMember", "IPhone")]
    [InlineData("aapl:IPadMember", "IPad")]
    [InlineData("aapl:MacMember", "Mac")]
    public void Humanize_LoneLeadingCapital_StaysAttachedToItsWord(string qname, string expected)
    {
        RevenueBreakdownTools.Humanize(qname).Should().Be(expected);
    }

    [Fact]
    public void Humanize_AcronymPrefixDeeperInName_StillSplits()
    {
        RevenueBreakdownTools.Humanize("x:USSegmentMember").Should().Be("US Segment");
    }

    [Fact]
    public void Humanize_PlainPascalCase_StillSplits()
    {
        RevenueBreakdownTools.Humanize("nvda:DataCenterMember").Should().Be("Data Center");
    }
}
