using System.Reflection;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.BusinessLogic.Models;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins the catalyst overlay (ShortSqueezeScoreManager.ApplyPercentiles): each
/// active catalyst adds its boost on top of the weighted base, the total boost is
/// capped at <see cref="ShortSqueezeScoreManager.MaxCatalystBoost"/>, and the
/// composite never exceeds 100 — the additive-event structure of the published
/// squeeze models.
/// </summary>
public class ShortSqueezeScoreManagerCatalystBoostTests
{
    [Fact]
    public void ApplyPercentiles_SingleCatalyst_AddsItsBoostToTheBase()
    {
        var quiet = Score(0.1m);
        var spiking = Score(0.5m);
        spiking.HasPriceSpikeCatalyst = true;

        InvokeApplyPercentiles([quiet, Score(0.9m), spiking]);

        spiking.BaseScore.Should().Be(50);
        spiking.CatalystBoost.Should().Be(ShortSqueezeScoreManager.PriceSpikeCatalystBoost);
        spiking.Score.Should().Be(50 + ShortSqueezeScoreManager.PriceSpikeCatalystBoost);
        quiet.CatalystBoost.Should().Be(0);
        quiet.Score.Should().Be(quiet.BaseScore);
    }

    [Fact]
    public void ApplyPercentiles_BothCatalysts_BoostIsCappedAtTheMaximum()
    {
        var both = Score(0.5m);
        both.HasPriceSpikeCatalyst = true;
        both.HasVolumeSurgeCatalyst = true;

        InvokeApplyPercentiles([Score(0.1m), Score(0.9m), both]);

        both.CatalystBoost.Should().Be(ShortSqueezeScoreManager.MaxCatalystBoost);
        both.Score.Should().Be(50 + ShortSqueezeScoreManager.MaxCatalystBoost);
    }

    [Fact]
    public void ApplyPercentiles_EarningsProximityAlone_AddsItsBoost()
    {
        var reporting = Score(0.5m);
        reporting.HasEarningsProximityCatalyst = true;

        InvokeApplyPercentiles([Score(0.1m), Score(0.9m), reporting]);

        reporting
            .CatalystBoost.Should()
            .Be(ShortSqueezeScoreManager.EarningsProximityCatalystBoost);
        reporting.Score.Should().Be(50 + ShortSqueezeScoreManager.EarningsProximityCatalystBoost);
    }

    [Fact]
    public void ApplyPercentiles_AllThreeCatalysts_StillCappedAtTheMaximum()
    {
        // 3 × 10 rank points of raw boost must not exceed the +20 ceiling the
        // published models cap event overlays at.
        var everything = Score(0.5m);
        everything.HasPriceSpikeCatalyst = true;
        everything.HasVolumeSurgeCatalyst = true;
        everything.HasEarningsProximityCatalyst = true;

        InvokeApplyPercentiles([Score(0.1m), Score(0.9m), everything]);

        everything.CatalystBoost.Should().Be(ShortSqueezeScoreManager.MaxCatalystBoost);
        everything.Score.Should().Be(50 + ShortSqueezeScoreManager.MaxCatalystBoost);
    }

    [Fact]
    public void ApplyPercentiles_BoostOnTopOfTheUniverse_ClampsTheCompositeAt100()
    {
        // The top-percentile stock already sits at 100; catalysts must not push the
        // composite past the scale's ceiling.
        var top = Score(0.9m);
        top.HasPriceSpikeCatalyst = true;
        top.HasVolumeSurgeCatalyst = true;

        InvokeApplyPercentiles([Score(0.1m), top]);

        top.BaseScore.Should().Be(100);
        top.CatalystBoost.Should().Be(ShortSqueezeScoreManager.MaxCatalystBoost);
        top.Score.Should().Be(100);
    }

    private static ShortSqueezeScore Score(decimal shortInterestPercentOfShares) =>
        new()
        {
            CommonStockId = Guid.NewGuid(),
            ShortInterestPercentOfShares = shortInterestPercentOfShares,
        };

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
