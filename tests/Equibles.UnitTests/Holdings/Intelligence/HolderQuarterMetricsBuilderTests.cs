using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderQuarterMetricsBuilderTests
{
    private static readonly DateOnly Quarter = new(2024, 12, 31);
    private static readonly DateOnly Previous = new(2024, 9, 30);
    private static readonly Guid Holder = Guid.NewGuid();
    private static readonly Guid Kept = Guid.NewGuid();
    private static readonly Guid Split = Guid.NewGuid();
    private static readonly Guid Dropped = Guid.NewGuid();
    private const decimal Tolerance = 0.0000001m;

    [Fact]
    public void Build_KeepsLegsOutOfTheEquityBookAndRestatesTheQuarterBefore()
    {
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current =
            [
                Row(Kept, Quarter, 100, 1_000),
                Row(Split, Quarter, 200, 2_000),
                Row(Kept, Quarter, 50, 300, optionType: OptionType.Call),
                Row(Kept, Quarter, 40, 200, optionType: OptionType.Put),
                Row(Dropped, Quarter, 10, 999, shareType: ShareType.Principal),
                Row(Guid.NewGuid(), Quarter, 5, 777, valueMissing: true),
            ],
            Previous = [Row(Kept, Previous, 100, 900), Row(Split, Previous, 100, 1_800)],
            // A 2-for-1 split inside the quarter: 100 old shares are 200 new ones, so no trade.
            PreviousShareFactors = new Dictionary<HolderSecurityKey, decimal?>
            {
                [new(Split, null)] = 2m,
            },
            Benchmarks = [Aggregate(new Dictionary<Guid, decimal> { [Kept] = 1, [Split] = 2 })],
        };

        var metrics = HolderQuarterMetricsBuilder.Build(input, 7).Metrics;

        metrics.InstitutionalHolderId.Should().Be(Holder);
        metrics.EquityAum.Should().Be(3_000);
        metrics.PreviousEquityAum.Should().Be(2_700);
        metrics.StockCount.Should().Be(3);
        metrics.NewCount.Should().Be(1);
        metrics.IncreasedCount.Should().Be(0);
        metrics.UnpricedTradeCount.Should().Be(1);
        metrics.Turnover.Should().Be(0m);
        metrics.CallValue.Should().Be(300);
        metrics.PutValue.Should().Be(200);
        metrics.PrincipalValue.Should().Be(999);
        metrics.AggregateActiveShare.Should().BeApproximately(0m, 0.0000001m);
        metrics.CalculationVersion.Should().Be(7);
        metrics.BandVersion.Should().Be(HolderStyleBandClassifier.BandVersion);
    }

    [Fact]
    public void Build_UnresolvedSplitBasis_LeavesTheTradeUnpriced()
    {
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(Split, Quarter, 300, 3_000)],
            Previous = [Row(Split, Previous, 100, 1_000)],
            PreviousShareFactors = new Dictionary<HolderSecurityKey, decimal?>
            {
                [new(Split, null)] = null,
            },
        };

        var metrics = HolderQuarterMetricsBuilder.Build(input, 1).Metrics;

        metrics.IncreasedCount.Should().Be(0);
        metrics.UnpricedTradeCount.Should().Be(1);
        metrics.BuyValue.Should().Be(0m);
    }

    [Fact]
    public void Build_NoFilingTheQuarterBefore_LeavesTradesAndTrailingTurnoverUnknown()
    {
        var metrics = HolderQuarterMetricsBuilder
            .Build(
                new HolderQuarterInput
                {
                    InstitutionalHolderId = Holder,
                    ReportDate = Quarter,
                    Current = [Row(Kept, Quarter, 100, 1_000)],
                    PriorTurnovers = [0.1m, 0.1m, 0.1m],
                },
                1
            )
            .Metrics;

        metrics.Turnover.Should().BeNull();
        metrics.NewCount.Should().BeNull();
        metrics.TrailingAnnualTurnover.Should().BeNull();
    }

    [Fact]
    public void Build_AddsThisQuarterToTheThreeBeforeForTrailingTurnover()
    {
        var metrics = HolderQuarterMetricsBuilder
            .Build(
                new HolderQuarterInput
                {
                    InstitutionalHolderId = Holder,
                    ReportDate = Quarter,
                    Current = [Row(Kept, Quarter, 100, 1_000), Row(Split, Quarter, 100, 1_000)],
                    Previous =
                    [
                        Row(Kept, Previous, 100, 1_000),
                        Row(Dropped, Previous, 100, 1_000),
                    ],
                    PriorTurnovers = [0.1m, 0.2m, 0.3m],
                },
                1
            )
            .Metrics;

        metrics.Turnover.Should().Be(0.5m);
        metrics.TrailingAnnualTurnover.Should().Be(1.1m);
        metrics.TurnoverBand.Should().Be(HolderTurnoverBand.High);
    }

    [Fact]
    public void Build_ReadsTenureFromStoredMasksAndReportsTheBitsToChange()
    {
        var quarter = HolderTenureCalculator.QuarterIndex(Quarter).Value;
        var stale = new HolderSecurityKey(Dropped, null);
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(Kept, Quarter, 100, 3_000), Row(Split, Quarter, 100, 1_000)],
            Previous = [Row(Kept, Previous, 100, 3_000)],
            Presence = new Dictionary<HolderSecurityKey, long>
            {
                // Held the two quarters before, and this quarter's bit already stored.
                [new(Kept, null)] = Mask(quarter - 2, quarter - 1, quarter),
                // A position the filer no longer holds still carries this quarter's bit.
                [stale] = Mask(quarter - 1, quarter),
            },
            FiledMask = Mask(quarter - 2, quarter - 1),
        };

        var result = HolderQuarterMetricsBuilder.Build(input, 1);

        result.PresenceToSet.Should().Equal(new HolderSecurityKey(Split, null));
        result.PresenceToClear.Should().Equal(stale);
        // 3,000 held three quarters and 1,000 held one.
        result.Metrics.ValueWeightedTenureQuarters.Should().Be(2.5m);
        result.Metrics.CensoredValueShare.Should().Be(0m);
        result.Metrics.HorizonBand.Should().Be(HolderHorizonBand.Short);
    }

    [Fact]
    public void Build_ReadsTheAdvisersIndexSeriesAssetsIntoTheBandAndKeepsThem()
    {
        // 120 equal positions far from the aggregate book: only the index-fund assets read index-like.
        var book = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = Enumerable
                .Range(0, 120)
                .Select(_ => Row(Guid.NewGuid(), Quarter, 1_000, 100_000))
                .ToList(),
            Benchmarks = [Aggregate(new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 1 })],
        };
        var asOf = new DateOnly(2024, 6, 30);

        var withoutSeries = HolderQuarterMetricsBuilder.Build(book, 1).Metrics;
        var withSeries = HolderQuarterMetricsBuilder
            .Build(
                book with
                {
                    IndexSeriesNetAssets = 9_000_000m,
                    AdvisedSeriesNetAssets = 10_000_000m,
                    AdvisedSeriesAsOf = asOf,
                },
                1
            )
            .Metrics;

        withoutSeries.StyleBand.Should().Be(HolderStyleBand.DiversifiedActive);
        withoutSeries.AdvisedSeriesAsOf.Should().BeNull();
        withSeries.StyleBand.Should().Be(HolderStyleBand.IndexLike);
        withSeries.IndexLikeEvidence.Should().Be(HolderIndexLikeEvidence.IndexFundAssets);
        withSeries.IndexSeriesNetAssets.Should().Be(9_000_000m);
        withSeries.AdvisedSeriesNetAssets.Should().Be(10_000_000m);
        withSeries.AdvisedSeriesAsOf.Should().Be(asOf);
    }

    [Fact]
    public void Build_TheFiledValueVouchesForAPendingRowsShares()
    {
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current =
            [
                Row(Kept, Quarter, 150, 0, valueMissing: true, filedValue: 1_500),
                Row(Split, Quarter, 10, 0, valueMissing: true),
            ],
            Previous = [Row(Kept, Previous, 100, 1_000)],
            AveragePrices = new Dictionary<HolderSecurityKey, decimal>
            {
                [new(Kept, null)] = 10m,
                [new(Split, null)] = 10m,
            },
        };

        var metrics = HolderQuarterMetricsBuilder.Build(input, 1).Metrics;

        metrics.BuyValue.Should().Be(500m);
        metrics
            .UnpricedTradeCount.Should()
            .Be(1, "a pending row with no filed value vouches nothing");
    }

    [Fact]
    public void Build_AQuarterOutsideCoverage_Throws()
    {
        var build = () =>
            HolderQuarterMetricsBuilder.Build(
                new HolderQuarterInput { ReportDate = new DateOnly(2019, 12, 31) },
                1
            );

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_MeasuresEveryBenchmarkAndBandsOnTheClosest()
    {
        // 120 equal positions that are the index, far from a combined book of other names.
        var issuers = Enumerable.Range(0, 120).Select(_ => Guid.NewGuid()).ToList();
        var weights = issuers.ToDictionary(issuer => issuer, _ => 1m);
        var asOf = new DateOnly(2024, 11, 30);
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = issuers.Select(issuer => Row(issuer, Quarter, 1_000, 100_000)).ToList(),
            Benchmarks =
            [
                Benchmark(HolderBenchmarkCatalog.Sp500, asOf, weights, coverage: 0.97m),
                Aggregate(new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 1 }),
            ],
        };

        var result = HolderQuarterMetricsBuilder.Build(input, 6);

        var metrics = result.Metrics;
        metrics.AggregateActiveShare.Should().BeApproximately(1m, Tolerance);
        metrics.ClosestBenchmark.Should().Be(HolderBenchmarkCatalog.Sp500);
        metrics.ClosestBenchmarkName.Should().Be("S&P 500");
        metrics.ClosestBenchmarkAsOf.Should().Be(asOf);
        metrics.ClosestActiveShare.Should().BeApproximately(0m, Tolerance);
        metrics.StyleBand.Should().Be(HolderStyleBand.IndexLike);
        metrics.IndexLikeEvidence.Should().Be(HolderIndexLikeEvidence.ActiveShare);

        result
            .BenchmarkActiveShares.Select(row => (row.BenchmarkKey, row.BenchmarkAsOf))
            .Should()
            .Equal(
                (HolderBenchmarkCatalog.Sp500, asOf),
                (HolderBenchmarkCatalog.Aggregate, Quarter)
            );
        var index = result.BenchmarkActiveShares[0];
        index.ActiveShare.Should().BeApproximately(0m, Tolerance);
        index.Overlap.Should().BeApproximately(1m, Tolerance);
        index.Coverage.Should().Be(0.97m);
        var aggregate = result.BenchmarkActiveShares[1];
        aggregate.ActiveShare.Should().Be(metrics.AggregateActiveShare.Value);
        aggregate.Overlap.Should().Be(0m);
        result
            .BenchmarkActiveShares.Should()
            .OnlyContain(row => row.InstitutionalHolderId == Holder && row.ReportDate == Quarter);
    }

    [Theory]
    [InlineData(0.90, 0.90, HolderBenchmarkCatalog.Russell2000)]
    [InlineData(0.8999, 0.90, HolderBenchmarkCatalog.Aggregate)]
    [InlineData(0.80, 0.80, HolderBenchmarkCatalog.Russell2000)]
    public void Build_ABenchmarkUnderTheCoverageFloorIsStoredButNeverTheClosest(
        double coverage,
        double floor,
        string closest
    )
    {
        // 120 equal positions that are the index, far from a combined book of other names.
        var issuers = Enumerable.Range(0, 120).Select(_ => Guid.NewGuid()).ToList();
        var weights = issuers.ToDictionary(issuer => issuer, _ => 1m);
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = issuers.Select(issuer => Row(issuer, Quarter, 1_000, 100_000)).ToList(),
            Benchmarks =
            [
                Benchmark(HolderBenchmarkCatalog.Russell2000, Quarter, weights, (decimal)coverage),
                Aggregate(new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 1 }),
            ],
            MinimumBenchmarkCoverage = (decimal)floor,
        };

        var result = HolderQuarterMetricsBuilder.Build(input, 6);

        result.Metrics.ClosestBenchmark.Should().Be(closest);
        result
            .Metrics.StyleBand.Should()
            .Be(
                closest == HolderBenchmarkCatalog.Aggregate
                    ? HolderStyleBand.DiversifiedActive
                    : HolderStyleBand.IndexLike
            );
        var index = result.BenchmarkActiveShares.Single(row =>
            row.BenchmarkKey == HolderBenchmarkCatalog.Russell2000
        );
        index.ActiveShare.Should().BeApproximately(0m, Tolerance);
        index.Coverage.Should().Be((decimal)coverage);
    }

    [Fact]
    public void Build_ATieKeepsThePublishedIndex()
    {
        var book = new Dictionary<Guid, decimal> { [Kept] = 1, [Split] = 1 };
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(Kept, Quarter, 10, 1_000), Row(Split, Quarter, 10, 1_000)],
            Benchmarks =
            [
                Benchmark(HolderBenchmarkCatalog.Russell2000, Quarter, book),
                Aggregate(book),
            ],
        };

        HolderQuarterMetricsBuilder
            .Build(input, 6)
            .Metrics.ClosestBenchmark.Should()
            .Be(HolderBenchmarkCatalog.Russell2000);
    }

    [Fact]
    public void Build_WithoutBenchmarks_LeavesActiveShareUnmeasured()
    {
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(Kept, Quarter, 10, 1_000)],
        };

        var result = HolderQuarterMetricsBuilder.Build(input, 6);

        result.Metrics.AggregateActiveShare.Should().BeNull();
        result.Metrics.ClosestBenchmark.Should().BeNull();
        result.Metrics.ClosestActiveShare.Should().BeNull();
        result.Metrics.SectorActiveShare.Should().BeNull();
        result.BenchmarkActiveShares.Should().BeEmpty();
    }

    [Fact]
    public void Build_SectorActiveShareComparesSectorWeightsWithTheCombinedBook()
    {
        var technology = Guid.NewGuid();
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            // Three quarters in technology, a quarter in an issuer without a sector.
            Current = [Row(Kept, Quarter, 10, 3_000), Row(Split, Quarter, 10, 1_000)],
            IssuerSectors = new Dictionary<Guid, Guid> { [Kept] = technology },
            AggregateSectorWeights = new Dictionary<Guid, decimal>
            {
                [technology] = 1,
                [HolderSectorWeights.Unclassified] = 1,
            },
        };

        HolderQuarterMetricsBuilder
            .Build(input, 6)
            .Metrics.SectorActiveShare.Should()
            .Be(0.25m, "half of |0.75 - 0.5| + |0.25 - 0.5|");
    }

    [Fact]
    public void Build_RecordsWhoseBookTheTradesCompareWith()
    {
        var predecessor = Guid.NewGuid();
        var input = new HolderQuarterInput
        {
            InstitutionalHolderId = Holder,
            ReportDate = Quarter,
            Current = [Row(Kept, Quarter, 150, 1_500)],
        };
        var previous = new List<HolderIntelligenceRow> { Row(Kept, Previous, 100, 1_000) };

        HolderQuarterMetricsBuilder.Build(input, 6).Metrics.ComparedHolderId.Should().BeNull();
        HolderQuarterMetricsBuilder
            .Build(input with { Previous = previous }, 6)
            .Metrics.ComparedHolderId.Should()
            .Be(Holder);
        var continued = HolderQuarterMetricsBuilder
            .Build(input with { Previous = previous, ComparedHolderId = predecessor }, 6)
            .Metrics;
        continued.ComparedHolderId.Should().Be(predecessor);
        continued.IncreasedCount.Should().Be(1, "the predecessor's book is the baseline");
        continued.PreviousEquityAum.Should().Be(1_000);
    }

    private static HolderBenchmark Aggregate(Dictionary<Guid, decimal> weights) =>
        Benchmark(HolderBenchmarkCatalog.Aggregate, Quarter, weights);

    private static HolderBenchmark Benchmark(
        string key,
        DateOnly asOf,
        Dictionary<Guid, decimal> weights,
        decimal coverage = 1m
    ) =>
        new()
        {
            Key = key,
            AsOf = asOf,
            Weights = weights,
            Coverage = coverage,
        };

    private static HolderIntelligenceRow Row(
        Guid issuer,
        DateOnly reportDate,
        long shares,
        long value,
        ShareType shareType = ShareType.Shares,
        OptionType? optionType = null,
        bool valueMissing = false,
        long? filedValue = null
    ) =>
        new()
        {
            InstitutionalHolderId = Holder,
            EquityIssuerId = issuer,
            ReportDate = reportDate,
            ShareType = shareType,
            OptionType = optionType,
            Shares = shares,
            Value = value,
            ValueMissing = valueMissing,
            FiledValue = filedValue,
        };

    private static long Mask(params int[] quarters) =>
        quarters.Aggregate(0L, HolderTenureCalculator.Set);
}
