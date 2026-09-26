using Equibles.Sec.Data.Helpers;

namespace Equibles.UnitTests.Sec;

public class FundHoldingsCoverageTests
{
    [Theory]
    [InlineData(504, 504, "fullPortfolio")]
    [InlineData(498, 519, "trackedEquitiesOnly")]
    [InlineData(504, null, "unknown")]
    [InlineData(505, 504, "unknown")]
    [InlineData(0, 0, "fullPortfolio")]
    [InlineData(0, 5, "trackedEquitiesOnly")]
    [InlineData(0, null, "unknown")]
    [InlineData(null, 0, "unknown")]
    public void CoverageRequiresKnownMatchingCounts(int? stored, int? reported, string expected)
    {
        FundHoldingsCoverage.Classify(stored, reported).Should().Be(expected);
    }
}
