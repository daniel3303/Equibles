using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderIdentifierRulesTests
{
    [Theory]
    [InlineData("000160108", 160108)]
    [InlineData(" 105958 ", 105958)]
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
}
