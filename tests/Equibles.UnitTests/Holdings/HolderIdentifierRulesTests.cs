using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderIdentifierRulesTests
{
    [Theory]
    [InlineData("000160108", 160108)]
    [InlineData(" 105958 ", 105958)]
    [InlineData("0000000160108", 160108)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("0", null)]
    [InlineData("N/A", null)]
    [InlineData("12-345", null)]
    [InlineData("1234567890", null)]
    public void ParseCrd_ReadsTheFiledNumber(string filed, int? expected)
    {
        HolderIdentifierRules.ParseCrd(filed).Should().Be(expected);
    }

    [Theory]
    [InlineData("801-0117266", "801-117266")]
    [InlineData("801-117266", "801-117266")]
    [InlineData("802-01234", "802-1234")]
    [InlineData(" 0801-56 ", "801-56")]
    [InlineData("028-12345", null)]
    [InlineData("803-12345", null)]
    [InlineData("801-0", null)]
    [InlineData("801", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeSecFileNumber_KeepsOnlyAdviserFileNumbers(string filed, string expected)
    {
        HolderIdentifierRules.NormalizeSecFileNumber(filed).Should().Be(expected);
    }

    [Theory]
    [InlineData("028-04295", "28-4295")]
    [InlineData("28-4295", "28-4295")]
    [InlineData(" 0028-04295 ", "28-4295")]
    [InlineData("801-117266", null)]
    [InlineData("028-0", null)]
    [InlineData("028-04295-01", null)]
    [InlineData("S028-04295", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeForm13FFileNumber_KeepsOnlyForm13FFileNumbers(
        string filed,
        string expected
    )
    {
        HolderIdentifierRules.NormalizeForm13FFileNumber(filed).Should().Be(expected);
    }

    [Fact]
    public void SummarizeAdvisedSeries_CountsEachSeriesOnceAtItsLatestReport()
    {
        var summaries = HolderIdentifierRules.SummarizeAdvisedSeries([
            new AdvisedSeriesRow(1, "S1", new DateOnly(2025, 12, 31), NCenFundTypes.None, 50m),
            new AdvisedSeriesRow(
                1,
                "S1",
                new DateOnly(2026, 6, 30),
                NCenFundTypes.IndexFund | NCenFundTypes.ExchangeTradedFund,
                60m
            ),
            new AdvisedSeriesRow(
                1,
                "S2",
                new DateOnly(2026, 3, 31),
                NCenFundTypes.MultipleOrInverseIndexFund,
                null
            ),
            new AdvisedSeriesRow(
                1,
                "S3",
                new DateOnly(2026, 3, 31),
                NCenFundTypes.ExchangeTradedManagedFund,
                10m
            ),
            new AdvisedSeriesRow(2, "S4", new DateOnly(2026, 3, 31), NCenFundTypes.None, null),
        ]);

        summaries[1]
            .Should()
            .Be(new AdvisedSeriesSummary(3, 2, 2, 70m, 60m, new DateOnly(2026, 6, 30)));
        // No reported net assets is unknown, never zero.
        summaries[2].NetAssets.Should().BeNull();
        summaries[2].IndexNetAssets.Should().BeNull();
    }

    [Fact]
    public void SummarizeAdvisedSeries_SeriesThatChangedAdviser_CountsOnlyForTheCurrentOne()
    {
        var summaries = HolderIdentifierRules.SummarizeAdvisedSeries([
            new AdvisedSeriesRow(100, "S1", new DateOnly(2025, 9, 30), NCenFundTypes.None, 50m),
            new AdvisedSeriesRow(200, "S1", new DateOnly(2026, 9, 30), NCenFundTypes.None, 60m),
            // Co-advisers named on one report both count it.
            new AdvisedSeriesRow(200, "S2", new DateOnly(2026, 6, 30), NCenFundTypes.None, 10m),
            new AdvisedSeriesRow(300, "S2", new DateOnly(2026, 6, 30), NCenFundTypes.None, 10m),
        ]);

        summaries.Keys.Should().BeEquivalentTo([200, 300]);
        summaries[200].SeriesCount.Should().Be(2);
        summaries[200].NetAssets.Should().Be(70m);
        summaries[300].SeriesCount.Should().Be(1);
    }

    [Fact]
    public void SummarizeAdvisedSeries_LatestReportWithoutAnAdviserCrd_CountsForNobody()
    {
        var summaries = HolderIdentifierRules.SummarizeAdvisedSeries([
            new AdvisedSeriesRow(100, "S1", new DateOnly(2025, 9, 30), NCenFundTypes.None, 50m),
            new AdvisedSeriesRow(null, "S1", new DateOnly(2026, 9, 30), NCenFundTypes.None, 60m),
        ]);

        summaries.Should().BeEmpty();
    }
}
