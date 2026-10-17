using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderStyleBandClassifierTests
{
    // A large diversified active book; each test moves one measurement across a threshold.
    private static readonly HolderStyleInputs Diversified = new()
    {
        EquityAum = 1_000_000_000,
        StockCount = 300,
        ClosestActiveShare = 0.70m,
        TrailingAnnualTurnover = 0.50m,
        TopTenWeight = 0.35m,
        EffectivePositions = 60m,
        ValueWeightedTenureQuarters = 8m,
        CensoredValueShare = 0m,
    };

    [Fact]
    public void Classify_ADiversifiedActiveBook()
    {
        HolderStyleBandClassifier
            .Classify(Diversified)
            .Should()
            .Be(
                new HolderStyleClassification(
                    HolderStyleBand.DiversifiedActive,
                    HolderIndexLikeEvidence.None,
                    HolderTurnoverBand.Medium,
                    HolderHorizonBand.Medium
                )
            );
    }

    [Theory]
    [InlineData(9_999_999L, 300, true)]
    [InlineData(10_000_000L, 4, true)]
    [InlineData(10_000_000L, 5, false)]
    public void Classify_BelowTheSizeFloor_IsUnknown(long aum, int stocks, bool unknown)
    {
        var style = Style(Diversified with { EquityAum = aum, StockCount = stocks });

        (style == HolderStyleBand.Unknown).Should().Be(unknown);
    }

    [Fact]
    public void Classify_NoValuedPosition_IsUnknown()
    {
        Style(Diversified with { TopTenWeight = null }).Should().Be(HolderStyleBand.Unknown);
    }

    [Theory]
    [InlineData(0.20, 100, HolderStyleBand.IndexLike)]
    [InlineData(0.2001, 100, HolderStyleBand.DiversifiedActive)]
    [InlineData(0.05, 99, HolderStyleBand.DiversifiedActive)]
    public void Classify_IndexLikeByActiveShare(
        double activeShare,
        int stocks,
        HolderStyleBand band
    )
    {
        var classification = HolderStyleBandClassifier.Classify(
            Diversified with
            {
                ClosestActiveShare = (decimal)activeShare,
                StockCount = stocks,
            }
        );

        classification.Style.Should().Be(band);
        classification
            .IndexLikeEvidence.Should()
            .Be(
                band == HolderStyleBand.IndexLike
                    ? HolderIndexLikeEvidence.ActiveShare
                    : HolderIndexLikeEvidence.None
            );
    }

    [Theory]
    [InlineData(500_000_000, 625_000_000, HolderStyleBand.IndexLike)]
    [InlineData(500_000_000, 625_000_001, HolderStyleBand.DiversifiedActive)]
    [InlineData(499_999_999, 600_000_000, HolderStyleBand.DiversifiedActive)]
    public void Classify_IndexLikeByIndexFundAssets(
        long indexAssets,
        long advisedAssets,
        HolderStyleBand band
    )
    {
        var classification = HolderStyleBandClassifier.Classify(
            Diversified with
            {
                IndexSeriesNetAssets = indexAssets,
                AdvisedSeriesNetAssets = advisedAssets,
            }
        );

        classification.Style.Should().Be(band);
        if (band == HolderStyleBand.IndexLike)
            classification.IndexLikeEvidence.Should().Be(HolderIndexLikeEvidence.IndexFundAssets);
    }

    [Fact]
    public void Classify_IndexFundAssetsStillNeedTheStockCount()
    {
        Style(
                Diversified with
                {
                    StockCount = 99,
                    IndexSeriesNetAssets = 900_000_000,
                    AdvisedSeriesNetAssets = 900_000_000,
                }
            )
            .Should()
            .Be(HolderStyleBand.DiversifiedActive);
    }

    [Theory]
    [InlineData(500, 1.00, 0.25, HolderStyleBand.SystematicLike)]
    [InlineData(499, 1.00, 0.25, HolderStyleBand.DiversifiedActive)]
    [InlineData(500, 0.99, 0.25, HolderStyleBand.DiversifiedActive)]
    [InlineData(500, 1.00, 0.2501, HolderStyleBand.DiversifiedActive)]
    public void Classify_SystematicLike(
        int stocks,
        double turnover,
        double topTen,
        HolderStyleBand band
    )
    {
        Style(
                Diversified with
                {
                    StockCount = stocks,
                    TrailingAnnualTurnover = (decimal)turnover,
                    TopTenWeight = (decimal)topTen,
                }
            )
            .Should()
            .Be(band);
    }

    [Fact]
    public void Classify_SystematicLikeNeedsAKnownTurnover()
    {
        Style(
                Diversified with
                {
                    StockCount = 800,
                    TrailingAnnualTurnover = null,
                    TopTenWeight = 0.1m,
                }
            )
            .Should()
            .Be(HolderStyleBand.DiversifiedActive);
    }

    [Theory]
    [InlineData(0.60, 60, HolderStyleBand.Concentrated)]
    [InlineData(0.30, 12, HolderStyleBand.Concentrated)]
    [InlineData(0.5999, 12.01, HolderStyleBand.DiversifiedActive)]
    public void Classify_Concentrated(double topTen, double effective, HolderStyleBand band)
    {
        Style(
                Diversified with
                {
                    TopTenWeight = (decimal)topTen,
                    EffectivePositions = (decimal)effective,
                }
            )
            .Should()
            .Be(band);
    }

    [Fact]
    public void Classify_AnIndexLikeBookOfAConcentratedIndexStaysIndexLike()
    {
        Style(Diversified with { ClosestActiveShare = 0.1m, TopTenWeight = 0.7m })
            .Should()
            .Be(HolderStyleBand.IndexLike);
    }

    [Theory]
    [InlineData(null, HolderTurnoverBand.Unknown)]
    [InlineData(0.2999, HolderTurnoverBand.Low)]
    [InlineData(0.30, HolderTurnoverBand.Medium)]
    [InlineData(0.9999, HolderTurnoverBand.Medium)]
    [InlineData(1.00, HolderTurnoverBand.High)]
    public void TurnoverBand_Thresholds(double? turnover, HolderTurnoverBand band)
    {
        HolderStyleBandClassifier.TurnoverBand((decimal?)turnover).Should().Be(band);
    }

    [Theory]
    [InlineData(null, HolderHorizonBand.Unknown)]
    [InlineData(3.99, HolderHorizonBand.Short)]
    [InlineData(4.0, HolderHorizonBand.Medium)]
    [InlineData(11.99, HolderHorizonBand.Medium)]
    [InlineData(12.0, HolderHorizonBand.Long)]
    public void HorizonBand_Thresholds(double? quarters, HolderHorizonBand band)
    {
        HolderStyleBandClassifier.HorizonBand((decimal?)quarters, 0m).Should().Be(band);
    }

    [Theory]
    [InlineData(3.0, 0.0, HolderHorizonBand.Short)]
    [InlineData(3.0, 0.01, HolderHorizonBand.Unknown)]
    [InlineData(8.0, 0.5, HolderHorizonBand.Unknown)]
    [InlineData(12.0, 0.5, HolderHorizonBand.Long)]
    [InlineData(8.0, null, HolderHorizonBand.Unknown)]
    public void HorizonBand_ACensoredTenureCanOnlyReadLong(
        double quarters,
        double? censoredShare,
        HolderHorizonBand band
    )
    {
        HolderStyleBandClassifier
            .Classify(
                Diversified with
                {
                    ValueWeightedTenureQuarters = (decimal)quarters,
                    CensoredValueShare = (decimal?)censoredShare,
                }
            )
            .HorizonBand.Should()
            .Be(band);
    }

    private static HolderStyleBand Style(HolderStyleInputs inputs) =>
        HolderStyleBandClassifier.Classify(inputs).Style;
}
