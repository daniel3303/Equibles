using System.Reflection;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.BusinessLogic.Models;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins the weighted composite (ShortSqueezeScoreManager.ApplyPercentiles): the
/// base score is the WEIGHTED mean of the available factor percentiles — short
/// interest 30%, days-to-cover 20%, price-above-VWAP 15%, short-volume trend 15%,
/// short-interest change 10%, fails-to-deliver 10% — with a missing factor's
/// weight redistributed over the factors present, never defaulted to a percentile.
/// </summary>
public class ShortSqueezeScoreManagerWeightedCompositeTests
{
    [Fact]
    public void ApplyPercentiles_AllFactorsPresent_ScoreIsWeightedMeanOfPercentiles()
    {
        // Three stocks with strictly ordered values on every factor, so each stock's
        // factor percentiles are 0 / 50 / 100 exactly. LOW leads only on short
        // interest (its other five percentiles are 0) and HIGH trails only on short
        // interest — pinning the 30% / 70% split between short interest and the rest.
        var leadsOnShortInterestOnly = FullyPopulatedScore(rank: 2);
        var middle = FullyPopulatedScore(rank: 1);
        var leadsOnEverythingElse = FullyPopulatedScore(rank: 0);
        // Flip the short-interest ordering so the "leads on everything else" stock
        // has the LOWEST short interest.
        leadsOnShortInterestOnly.ShortInterestPercentOfShares = 0.9m;
        middle.ShortInterestPercentOfShares = 0.5m;
        leadsOnEverythingElse.ShortInterestPercentOfShares = 0.1m;

        var scores = new List<ShortSqueezeScore>
        {
            leadsOnShortInterestOnly,
            middle,
            leadsOnEverythingElse,
        };

        InvokeApplyPercentiles(scores);

        // Weighted means: 0.30×100 = 30 · all-50 = 50 · 0.70×100 = 70.
        leadsOnShortInterestOnly.BaseScore.Should().Be(30);
        middle.BaseScore.Should().Be(50);
        leadsOnEverythingElse.BaseScore.Should().Be(70);
        // No catalysts flagged, so the composite equals the base.
        leadsOnShortInterestOnly.Score.Should().Be(30);
        leadsOnEverythingElse.Score.Should().Be(70);
    }

    [Fact]
    public void ApplyPercentiles_OnlyShortInterestAndDaysToCover_WeightsRenormalizeToTheirRatio()
    {
        // Two stocks with opposite orderings on the only two factors present: the
        // 30/20 weights must renormalize to 60/40 of the two-factor composite.
        var highShortInterest = new ShortSqueezeScore
        {
            CommonStockId = Guid.NewGuid(),
            ShortInterestPercentOfShares = 0.9m,
            DaysToCover = 1m,
        };
        var highDaysToCover = new ShortSqueezeScore
        {
            CommonStockId = Guid.NewGuid(),
            ShortInterestPercentOfShares = 0.1m,
            DaysToCover = 9m,
        };

        InvokeApplyPercentiles([highShortInterest, highDaysToCover]);

        // (0.30×100 + 0.20×0) / 0.50 = 60 and (0.30×0 + 0.20×100) / 0.50 = 40.
        highShortInterest.BaseScore.Should().Be(60);
        highDaysToCover.BaseScore.Should().Be(40);
    }

    [Fact]
    public void ApplyPercentiles_MissingFactors_StayNullAndDropOut()
    {
        // A stock with NOTHING but short interest must score on it alone (weight
        // renormalizes to 100%), and every absent factor keeps a null percentile.
        var bare = new ShortSqueezeScore
        {
            CommonStockId = Guid.NewGuid(),
            ShortInterestPercentOfShares = 0.9m,
        };
        var full = FullyPopulatedScore(rank: 0);
        full.ShortInterestPercentOfShares = 0.1m;

        InvokeApplyPercentiles([bare, full]);

        bare.DaysToCoverPercentile.Should().BeNull();
        bare.ShortVolumeTrendPercentile.Should().BeNull();
        bare.ShortInterestChangePercentile.Should().BeNull();
        bare.FailsToDeliverPercentile.Should().BeNull();
        bare.PriceAboveVwapPercentile.Should().BeNull();
        bare.BaseScore.Should().Be(bare.ShortInterestPercentile);
    }

    // A score with every factor populated, ordered by `rank` (0 = highest value on
    // every factor, so rank 0 tops every percentile set it shares with lower ranks).
    private static ShortSqueezeScore FullyPopulatedScore(int rank)
    {
        var level = 3 - rank; // rank 0 → 3 (highest), rank 2 → 1 (lowest)
        return new ShortSqueezeScore
        {
            CommonStockId = Guid.NewGuid(),
            ShortInterestPercentOfShares = level * 0.1m,
            DaysToCover = level,
            ShortVolumeShareTrend = level * 0.05m,
            ShortInterestChangePercent = level * 0.2m,
            FailsToDeliverPercentOfShares = level * 0.01m,
            PriceAboveVwap = level * 0.1m,
        };
    }

    private static void InvokeApplyPercentiles(List<ShortSqueezeScore> scores)
    {
        var method = typeof(ShortSqueezeScoreManager).GetMethod(
            "ApplyPercentiles",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull();
        method!.Invoke(null, [scores]);
    }
}
