using System.Reflection;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.BusinessLogic.Models;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Contract (ShortSqueezeScoreManager.ApplyPercentiles): days-to-cover and
/// short-volume trend are optional metrics. A score missing one is excluded
/// from that metric's peer-relative percentile set (its percentile stays
/// null) and contributes one fewer factor to the composite Score — so a score
/// with BOTH optionals null must score on short-interest alone and must not
/// throw. Pins the `.Where(x != null).Select(x.Value)` null-guard against a
/// regression that drops the filter (the case CodeQL flags as a null deref).
/// </summary>
public class ShortSqueezeScoreManagerApplyPercentilesNullMetricsTests
{
    [Fact]
    public void ApplyPercentiles_ScoreMissingBothOptionalMetrics_ScoresOnShortInterestAloneWithoutThrowing()
    {
        // s1 has the distinct-max short interest and no optional metrics; s2/s3
        // carry days-to-cover so the optional percentile set is non-empty.
        var s1 = new ShortSqueezeScore
        {
            CommonStockId = Guid.NewGuid(),
            ShortInterestPercentOfShares = 30m,
            DaysToCover = null,
            ShortVolumeShareTrend = null,
        };
        var s2 = new ShortSqueezeScore
        {
            CommonStockId = Guid.NewGuid(),
            ShortInterestPercentOfShares = 10m,
            DaysToCover = 5m,
            ShortVolumeShareTrend = null,
        };
        var s3 = new ShortSqueezeScore
        {
            CommonStockId = Guid.NewGuid(),
            ShortInterestPercentOfShares = 20m,
            DaysToCover = 15m,
            ShortVolumeShareTrend = 0.5m,
        };
        var scores = new List<ShortSqueezeScore> { s1, s2, s3 };

        var method = typeof(ShortSqueezeScoreManager).GetMethod(
            "ApplyPercentiles",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull();

        method!.Invoke(null, [scores]);

        // s1's null optionals are excluded, not defaulted to a percentile.
        s1.DaysToCoverPercentile.Should().BeNull();
        s1.ShortVolumeTrendPercentile.Should().BeNull();
        // With both optionals absent the composite is short-interest alone.
        s1.Score.Should().Be(s1.ShortInterestPercentile);
        // A present optional is still ranked: s3 is the distinct max of {5,15}.
        s3.DaysToCoverPercentile.Should().Be(100);
    }
}
