using Equibles.Holdings.BusinessLogic.FundOwnership;
using Equibles.Holdings.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings.FundOwnership;

public class FundOwnershipBucketsTests
{
    [Theory]
    [InlineData(NCenFundTypes.IndexFund, false, FundOwnershipBucket.IndexFund)]
    [InlineData(
        NCenFundTypes.IndexFund | NCenFundTypes.ExchangeTradedFund,
        false,
        FundOwnershipBucket.IndexEtf
    )]
    [InlineData(NCenFundTypes.IndexFund, true, FundOwnershipBucket.IndexEtf)]
    [InlineData(NCenFundTypes.ExchangeTradedManagedFund, false, FundOwnershipBucket.ActiveEtf)]
    [InlineData(NCenFundTypes.None, true, FundOwnershipBucket.ActiveEtf)]
    [InlineData(NCenFundTypes.None, false, FundOwnershipBucket.ActiveFund)]
    [InlineData(NCenFundTypes.TargetDateFund, false, FundOwnershipBucket.ActiveFund)]
    public void Classify_ReadsItemC3AndTheExchangeListing(
        NCenFundTypes types,
        bool listed,
        FundOwnershipBucket expected
    ) => FundOwnershipBuckets.Classify(types, listed).Should().Be(expected);

    [Fact]
    public void Classify_AMultipleOrInverseFundIsNotAnIndexFund()
    {
        FundOwnershipBuckets
            .Classify(
                NCenFundTypes.MultipleOrInverseIndexFund | NCenFundTypes.ExchangeTradedFund,
                false
            )
            .Should()
            .Be(FundOwnershipBucket.ActiveEtf);
        FundOwnershipBuckets
            .Classify(
                NCenFundTypes.MultipleOrInverseIndexFund
                    | NCenFundTypes.IndexFund
                    | NCenFundTypes.ExchangeTradedFund,
                false
            )
            .Should()
            .Be(FundOwnershipBucket.IndexEtf);
    }

    [Fact]
    public void Classify_NoReportIsUnclassifiedWhateverTheListing()
    {
        FundOwnershipBuckets.Classify(null, true).Should().Be(FundOwnershipBucket.Unclassified);
    }

    [Fact]
    public void IsIndex_CountsOnlyIndexFundsAndIndexEtfs()
    {
        Enum.GetValues<FundOwnershipBucket>()
            .Where(FundOwnershipBuckets.IsIndex)
            .Should()
            .Equal(FundOwnershipBucket.IndexFund, FundOwnershipBucket.IndexEtf);
    }

    [Fact]
    public void BuildQuarter_KeepsEveryBucketAndDividesByTodaysSharesOutstanding()
    {
        var quarter = new DateOnly(2026, 6, 30);
        var quarterView = FundOwnershipReader.BuildQuarter(
            quarter,
            null,
            [
                Row(quarter, FundOwnershipBucket.IndexEtf, 300, 2, new(2026, 5, 31)),
                Row(quarter, FundOwnershipBucket.IndexFund, 100, 1, new(2026, 6, 30)),
            ],
            row => row.Shares * 2,
            10_000,
            splitUnresolved: false
        );

        quarterView
            .Buckets.Select(bucket => bucket.Bucket)
            .Should()
            .Equal(
                FundOwnershipBucket.IndexFund,
                FundOwnershipBucket.IndexEtf,
                FundOwnershipBucket.ActiveEtf,
                FundOwnershipBucket.ActiveFund,
                FundOwnershipBucket.Unclassified
            );
        quarterView.Bucket(FundOwnershipBucket.IndexEtf).Shares.Should().Be(600);
        quarterView.Bucket(FundOwnershipBucket.IndexEtf).PercentOfOutstanding.Should().Be(6m);
        quarterView.Bucket(FundOwnershipBucket.ActiveFund).PercentOfOutstanding.Should().Be(0m);
        quarterView.Bucket(FundOwnershipBucket.ActiveFund).NewestReportPeriod.Should().BeNull();
        quarterView.IndexPercentOfOutstanding.Should().Be(8m);
        quarterView.Total.SeriesCount.Should().Be(3);
        quarterView.Total.OldestReportPeriod.Should().Be(new DateOnly(2026, 5, 31));
        quarterView.Total.NewestReportPeriod.Should().Be(quarter);
    }

    [Fact]
    public void BuildQuarter_WithoutSharesOutstandingGivesNoPercentages()
    {
        var quarter = new DateOnly(2026, 6, 30);
        var quarterView = FundOwnershipReader.BuildQuarter(
            quarter,
            null,
            [Row(quarter, FundOwnershipBucket.IndexFund, 100, 1, quarter)],
            row => row.Shares,
            null,
            splitUnresolved: true
        );

        quarterView.Total.PercentOfOutstanding.Should().BeNull();
        quarterView.IndexPercentOfOutstanding.Should().BeNull();
        quarterView.IndexShares.Should().Be(100);
    }

    private static StockQuarterlyFundOwnership Row(
        DateOnly quarter,
        FundOwnershipBucket bucket,
        long shares,
        int series,
        DateOnly period
    ) =>
        new()
        {
            ReportDate = quarter,
            Bucket = bucket,
            Shares = shares,
            SeriesCount = series,
            OldestReportPeriod = period,
            NewestReportPeriod = period,
        };
}
