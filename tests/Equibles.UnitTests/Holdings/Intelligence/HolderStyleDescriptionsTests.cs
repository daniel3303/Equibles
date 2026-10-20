using System.Globalization;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderStyleDescriptionsTests
{
    // A diversified active quarter; each test sets the band under test and its measurements.
    private static HolderQuarterlyMetrics Metrics() =>
        new()
        {
            EquityAum = 1_000_000_000,
            StockCount = 300,
            AggregateActiveShare = 0.70m,
            TrailingAnnualTurnover = 0.50m,
            TopTenWeight = 0.35m,
            EffectivePositions = 60m,
            ValueWeightedTenureQuarters = 8m,
            CensoredValueShare = 0m,
            StyleBand = HolderStyleBand.DiversifiedActive,
            TurnoverBand = HolderTurnoverBand.Medium,
            HorizonBand = HolderHorizonBand.Medium,
        };

    [Fact]
    public void Style_IndexLikeByActiveShare_NamesTheBenchmarkAndThreshold()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.IndexLike;
        metrics.IndexLikeEvidence = HolderIndexLikeEvidence.ActiveShare;
        metrics.AggregateActiveShare = 0.142m;
        metrics.StockCount = 3_456;

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be(
                "Index-like: active share 14.2% against every 13F filer's combined book, across 3,456 stocks (index-like at 20% or less with 100+ stocks)"
            );
    }

    [Fact]
    public void Style_IndexLikeByTheClosestIndex_NamesItAndItsDate()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.IndexLike;
        metrics.IndexLikeEvidence = HolderIndexLikeEvidence.ActiveShare;
        metrics.AggregateActiveShare = 0.52m;
        metrics.ClosestBenchmark = "russell-2000";
        metrics.ClosestBenchmarkName = "Russell 2000";
        metrics.ClosestBenchmarkAsOf = new DateOnly(2026, 5, 31);
        metrics.ClosestActiveShare = 0.031m;
        metrics.StockCount = 1_912;

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be(
                "Index-like: active share 3.1% against the Russell 2000 as of 2026-05-31, across 1,912 stocks (index-like at 20% or less with 100+ stocks)"
            );
    }

    [Fact]
    public void Style_IndexLikeByTheCombinedBook_KeepsItsWording()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.IndexLike;
        metrics.IndexLikeEvidence = HolderIndexLikeEvidence.ActiveShare;
        metrics.ClosestBenchmark = "13f-aggregate";
        metrics.ClosestBenchmarkName = "every 13F filer's combined book";
        metrics.ClosestBenchmarkAsOf = new DateOnly(2026, 6, 30);
        metrics.ClosestActiveShare = 0.142m;
        metrics.StockCount = 3_456;

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be(
                "Index-like: active share 14.2% against every 13F filer's combined book, across 3,456 stocks (index-like at 20% or less with 100+ stocks)"
            );
    }

    [Fact]
    public void BenchmarkSummary_NamesTheClosestBenchmarkAndSectorActiveShare()
    {
        var metrics = Metrics();
        metrics.ClosestBenchmark = "sp-500";
        metrics.ClosestBenchmarkName = "S&P 500";
        metrics.ClosestBenchmarkAsOf = new DateOnly(2026, 6, 30);
        metrics.ClosestActiveShare = 0.412m;
        metrics.SectorActiveShare = 0.087m;

        HolderStyleDescriptions
            .BenchmarkSummary(metrics)
            .Should()
            .Be(
                "Closest benchmark: the S&P 500 as of 2026-06-30, active share 41.2%; sector active share 8.7% against the combined 13F book's sector weights"
            );
    }

    [Theory]
    [InlineData(0.70)]
    [InlineData(null)]
    public void BenchmarkSummary_ARowNotYetRecomputedSaysSo(double? aggregateActiveShare)
    {
        // A row measured before index benchmarks has only the combined-book active share, which
        // is not the closest benchmark and must never read as one.
        var metrics = Metrics();
        metrics.AggregateActiveShare = (decimal?)aggregateActiveShare;

        HolderStyleDescriptions
            .BenchmarkSummary(metrics)
            .Should()
            .Be("Closest benchmark: not yet computed");
    }

    [Fact]
    public void BenchmarkSummary_ABookWithNoValuedCommonSharesHasNothingToCompare()
    {
        // No recompute measures such a book, so it must never read as waiting for one.
        var metrics = Metrics();
        metrics.EquityAum = 0;
        metrics.StockCount = 0;
        metrics.AggregateActiveShare = null;

        HolderStyleDescriptions
            .BenchmarkSummary(metrics)
            .Should()
            .Be("Closest benchmark: no common-share positions to compare");
    }

    [Fact]
    public void Style_IndexLikeByIndexFundAssets_StatesBothSharesAndTheNCenDate()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.IndexLike;
        metrics.IndexLikeEvidence = HolderIndexLikeEvidence.IndexFundAssets;
        metrics.IndexSeriesNetAssets = 900_000_000m;
        metrics.AdvisedSeriesNetAssets = 1_000_000_000m;
        metrics.AdvisedSeriesAsOf = new DateOnly(2026, 3, 31);

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be(
                "Index-like: index series hold 90.0% of the $1,000,000,000 in registered funds it advises (80%+), equal to 90.0% of its 13F equity book (50%+), across 300 stocks (100+), N-CEN as of 2026-03-31"
            );
    }

    [Fact]
    public void Style_SystematicLike_StatesAllThreeTests()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.SystematicLike;
        metrics.StockCount = 1_234;
        metrics.TrailingAnnualTurnover = 1.45m;
        metrics.TopTenWeight = 0.12m;

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be(
                "Systematic-like: 1,234 stocks (500+), trailing annual turnover 145.0% (100%+), top 10 weight 12.0% (25% or less)"
            );
    }

    [Fact]
    public void Style_ConcentratedByTopTenWeight_CitesTheWeight()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.Concentrated;
        metrics.TopTenWeight = 0.60m;

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be("Concentrated: the top 10 positions are 60.0% of the book (60%+)");
    }

    [Fact]
    public void Style_ConcentratedByEffectivePositions_CitesTheCount()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.Concentrated;
        metrics.TopTenWeight = 0.5999m;
        metrics.EffectivePositions = 8.34m;

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be("Concentrated: 8.3 effective positions (12 or fewer)");
    }

    [Fact]
    public void Style_DiversifiedActive_SaysNoTestWasMet()
    {
        HolderStyleDescriptions
            .Style(Metrics())
            .Should()
            .Be(
                "Diversified active: top 10 weight 35.0%, 60.0 effective positions, active share 70.0% against every 13F filer's combined book; it meets none of the index-like, systematic-like or concentrated tests"
            );
    }

    [Fact]
    public void Style_DiversifiedActiveWithoutActiveShare_LeavesItOut()
    {
        var metrics = Metrics();
        metrics.AggregateActiveShare = null;

        HolderStyleDescriptions.Style(metrics).Should().NotContain("active share");
    }

    [Fact]
    public void Style_UnknownBelowTheValueFloor_StatesTheFloor()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.Unknown;
        metrics.EquityAum = 9_999_999;

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be(
                "Unknown: $9,999,999 of valued equity, under the $10,000,000 needed to band a style"
            );
    }

    [Fact]
    public void Style_UnknownWithTooFewStocks_StatesTheStockFloor()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.Unknown;
        metrics.StockCount = 4;

        HolderStyleDescriptions
            .Style(metrics)
            .Should()
            .Be("Unknown: 4 stocks, under the 5 needed to band a style");
    }

    [Fact]
    public void Style_UnknownWithNoValuedPositions_SaysSo()
    {
        var metrics = Metrics();
        metrics.StyleBand = HolderStyleBand.Unknown;
        metrics.TopTenWeight = null;

        HolderStyleDescriptions.Style(metrics).Should().Be("Unknown: no valued positions to weigh");
    }

    [Theory]
    [InlineData(
        HolderTurnoverBand.Low,
        "Low turnover: 50.0% over the last four quarters (under 30%)"
    )]
    [InlineData(
        HolderTurnoverBand.Medium,
        "Medium turnover: 50.0% over the last four quarters (30% to under 100%)"
    )]
    [InlineData(
        HolderTurnoverBand.High,
        "High turnover: 50.0% over the last four quarters (100%+)"
    )]
    [InlineData(
        HolderTurnoverBand.Unknown,
        "Turnover unknown: it needs four straight quarters of measured turnover"
    )]
    public void Turnover_StatesTheTrailingRateAndItsBand(HolderTurnoverBand band, string expected)
    {
        var metrics = Metrics();
        metrics.TurnoverBand = band;

        HolderStyleDescriptions.Turnover(metrics).Should().Be(expected);
    }

    [Theory]
    [InlineData(
        HolderHorizonBand.Short,
        "Short horizon: value-weighted holding period 8.0 quarters (under 4)"
    )]
    [InlineData(
        HolderHorizonBand.Medium,
        "Medium horizon: value-weighted holding period 8.0 quarters (4 to under 12)"
    )]
    [InlineData(
        HolderHorizonBand.Long,
        "Long horizon: value-weighted holding period 8.0 quarters (12+)"
    )]
    public void Horizon_StatesTheHoldingPeriodAndItsBand(HolderHorizonBand band, string expected)
    {
        var metrics = Metrics();
        metrics.HorizonBand = band;

        HolderStyleDescriptions.Horizon(metrics).Should().Be(expected);
    }

    [Fact]
    public void Horizon_LongButCensored_CallsThePeriodAFloor()
    {
        var metrics = Metrics();
        metrics.HorizonBand = HolderHorizonBand.Long;
        metrics.ValueWeightedTenureQuarters = 14.2m;
        metrics.CensoredValueShare = 0.4m;

        HolderStyleDescriptions
            .Horizon(metrics)
            .Should()
            .Be(
                "Long horizon: value-weighted holding period 14.2 quarters (12+), a floor since 40.0% of the book was already held when coverage starts"
            );
    }

    [Fact]
    public void Horizon_UnknownBecauseCensored_ExplainsWhy()
    {
        var metrics = Metrics();
        metrics.HorizonBand = HolderHorizonBand.Unknown;
        metrics.ValueWeightedTenureQuarters = 6.1m;
        metrics.CensoredValueShare = 0.25m;

        HolderStyleDescriptions
            .Horizon(metrics)
            .Should()
            .Be(
                "Horizon unknown: the value-weighted holding period is at least 6.1 quarters, but 25.0% of the book was already held when coverage starts"
            );
    }

    [Fact]
    public void Horizon_UnknownWithoutValuedPositions_SaysSo()
    {
        var metrics = Metrics();
        metrics.HorizonBand = HolderHorizonBand.Unknown;
        metrics.ValueWeightedTenureQuarters = null;
        metrics.CensoredValueShare = null;

        HolderStyleDescriptions
            .Horizon(metrics)
            .Should()
            .Be("Horizon unknown: no valued positions");
    }

    [Fact]
    public void Methodology_StatesEveryThresholdFromTheClassifier()
    {
        HolderStyleDescriptions
            .Methodology.Should()
            .Contain(
                "tested in this order. Unknown: under $10,000,000 of valued equity or 5 stocks."
            )
            .And.Contain(
                "Index-like (100+ stocks): active share of 20% or less against the closest benchmark, which is the S&P 500, S&P MidCap 400, S&P SmallCap 600, Russell 1000, Russell 2000, Nasdaq-100 or Dow Jones Industrial Average (weighted by an index-tracking fund's reported holdings or the index's constituents nearest the quarter end, over the names resolved to companies, and only when those are 90%+ of its weight) or every 13F filer's combined book, or index series holding 80%+"
            )
            .And.Contain("equal to 50%+ of its book")
            .And.Contain("500+ stocks, 100%+ annual turnover and a top 10 weight of 25% or less")
            .And.Contain("a top 10 weight of 60%+ or 12 or fewer effective positions")
            .And.Contain("low under 30%, high 100%+")
            .And.Contain("short under 4, long 12+");
    }

    [Fact]
    public void Descriptions_AreCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var metrics = Metrics();
            metrics.StyleBand = HolderStyleBand.IndexLike;
            metrics.IndexLikeEvidence = HolderIndexLikeEvidence.IndexFundAssets;
            metrics.IndexSeriesNetAssets = 900_000_000m;
            metrics.AdvisedSeriesNetAssets = 1_000_000_000m;
            metrics.AdvisedSeriesAsOf = new DateOnly(2026, 3, 31);

            HolderStyleDescriptions
                .Style(metrics)
                .Should()
                .Contain("90.0% of the $1,000,000,000")
                .And.EndWith("N-CEN as of 2026-03-31");
            HolderStyleDescriptions.Methodology.Should().Contain("$10,000,000");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
