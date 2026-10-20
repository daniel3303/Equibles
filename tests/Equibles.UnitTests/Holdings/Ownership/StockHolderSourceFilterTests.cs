using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.BusinessLogic.Ownership.Models;

namespace Equibles.UnitTests.Holdings.Ownership;

public class StockHolderSourceFilterTests
{
    [Theory]
    [InlineData(null, "all")]
    [InlineData(" 13DG ", "13dg")]
    [InlineData("NonUs", "nonus")]
    [InlineData("13f-hr", null)]
    public void Normalize_AcceptsOnlyTheListedTokens(string token, string expected)
    {
        StockHolderSourceFilter.Normalize(token).Should().Be(expected);
    }

    [Fact]
    public void EverySourceHasItsOwnTokenAndAllMatchesEveryOne()
    {
        foreach (var source in Enum.GetValues<StockHolderSource>())
        {
            StockHolderSourceFilter.Matches(source, StockHolderSourceFilter.All).Should().BeTrue();
            StockHolderSourceFilter
                .Tokens.Where(token => token != StockHolderSourceFilter.All && token != "13dg")
                .Count(token => StockHolderSourceFilter.Matches(source, token))
                .Should()
                .Be(1, $"{source} needs exactly one single-source token");
        }
        StockHolderSourceFilter.Matches(StockHolderSource.Schedule13G, "13dg").Should().BeTrue();
        StockHolderSourceFilter.Matches(StockHolderSource.Fund, "nonus").Should().BeFalse();
    }
}
