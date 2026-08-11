using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

public class HoldingsCorpusCoverageTests
{
    [Theory]
    [InlineData("2020-01-01", "2020-03-31")]
    [InlineData("2020-03-31", "2020-03-31")]
    [InlineData("2020-04-01", "2020-06-30")]
    [InlineData("2020-12-15", "2020-12-31")]
    public void FirstQuarterEndOnOrAfter_MapsIngestFloorToCompleteReportQuarter(
        string floor,
        string expected
    )
    {
        HoldingsCorpusCoverage
            .FirstQuarterEndOnOrAfter(DateOnly.Parse(floor))
            .Should()
            .Be(DateOnly.Parse(expected));
    }

    [Theory]
    [InlineData("2020-03-31", "2019-12-31")]
    [InlineData("2020-06-30", "2020-03-31")]
    [InlineData("2020-09-30", "2020-06-30")]
    [InlineData("2020-12-31", "2020-09-30")]
    public void PreviousQuarterEnd_ReturnsTheActualCalendarQuarterEnd(
        string current,
        string expected
    )
    {
        HoldingsCorpusCoverage
            .PreviousQuarterEnd(DateOnly.Parse(current))
            .Should()
            .Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void Evaluate_TargetBeforeCoverage_FlagsIncompleteRanking()
    {
        var sut = new HoldingsCorpusCoverage(new DateOnly(2020, 1, 1));

        var status = sut.Evaluate(new DateOnly(2019, 12, 31), new DateOnly(2019, 9, 30));

        status.CoverageStartDate.Should().Be(new DateOnly(2020, 3, 31));
        status.IsWithinCoverage.Should().BeFalse();
        status.ComparisonAvailable.Should().BeFalse();
        status.ComparisonUnavailableReason.Should().Contain("outside the complete corpus");
    }

    [Fact]
    public void Evaluate_FirstCoveredQuarter_NullsComparisonAgainstSparsePrior()
    {
        var sut = new HoldingsCorpusCoverage(new DateOnly(2020, 1, 1));

        var status = sut.Evaluate(new DateOnly(2020, 3, 31), new DateOnly(2019, 12, 31));

        status.IsWithinCoverage.Should().BeTrue();
        status.ComparisonAvailable.Should().BeFalse();
        status.ComparisonUnavailableReason.Should().Contain("prior 2019-12-31");
    }

    [Fact]
    public void Evaluate_TwoCoveredQuarters_AllowsComparison()
    {
        var sut = new HoldingsCorpusCoverage(new DateOnly(2020, 1, 1));

        var status = sut.Evaluate(new DateOnly(2020, 6, 30), new DateOnly(2020, 3, 31));

        status.IsWithinCoverage.Should().BeTrue();
        status.ComparisonAvailable.Should().BeTrue();
        status.ComparisonUnavailableReason.Should().BeNull();
    }
}
