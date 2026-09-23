using Equibles.Sec.BusinessLogic.Search;

namespace Equibles.UnitTests.Sec;

// The shortened-query pass is the last keyword attempt after every full-length pass timed out.
// The queries below are corpus-wide searches that timed out in production on 2026-09-23.
public class Bm25QueryReducerTests
{
    [Theory]
    [InlineData(
        "Alberta Energy Regulator mandatory closure spend requirement 2026 industry-wide inventory reduction",
        "Alberta Energy Regulator 2026"
    )]
    [InlineData("discount to PDP PV-10 share price valuation", "discount PDP PV-10 valuation")]
    [InlineData(
        "plugging and abandonment asset retirement obligations acquisition price mature wells low decline valuation",
        "abandonment retirement obligations acquisition"
    )]
    public void KeepsTheMostSpecificTermsInTheCallersOrder(string query, string expected)
    {
        Assert.Equal(expected, Bm25QueryReducer.Reduce(query));
    }

    [Theory]
    [InlineData("2026 guidance")]
    [InlineData("the revenue of the segment")]
    [InlineData("helium Montana Kevin Dome")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ShortQueryHasNothingToShorten(string query)
    {
        Assert.Null(Bm25QueryReducer.Reduce(query));
    }

    [Fact]
    public void DropsStopWordsPunctuationAndRepeats()
    {
        Assert.Equal(
            "Kevin Dome helium Montana",
            Bm25QueryReducer.Reduce("Kevin Dome, the helium of Montana: helium, dome and gas?")
        );
    }

    [Theory]
    [InlineData(
        "firms that do NOT hedge Alberta 2026 commodity exposure",
        "Alberta 2026 commodity exposure"
    )]
    [InlineData(
        "\"going concern\" (Content:doubt) +substantial -waiver Nasdaq",
        "concern Content substantial Nasdaq"
    )]
    [InlineData("“Kevin Dome” helium Montana royalty acreage", "Kevin Dome Montana royalty")]
    [InlineData(
        "Société Générale capital ratio CET1 buffer requirement",
        "Société Générale CET1 requirement"
    )]
    [InlineData(
        "Socie\u0301te\u0301 Ge\u0301ne\u0301rale capital ratio CET1 buffer requirement",
        "Socie\u0301te\u0301 Ge\u0301ne\u0301rale CET1 requirement"
    )]
    public void NeverPassesQuerySyntaxOrAnOperatorToTheParser(string query, string expected)
    {
        Assert.Equal(expected, Bm25QueryReducer.Reduce(query));
    }
}
