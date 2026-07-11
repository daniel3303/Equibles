using Equibles.Finra.BusinessLogic;
using Equibles.Finra.BusinessLogic.Models;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins the shorts-underwater proxy (ShortSqueezePriceFactorCalculator): price
/// versus the trailing volume-weighted average — zero on a flat series, positive
/// when the last close trades above where the volume changed hands, null when the
/// series is too short (fewer than <see cref="ShortSqueezePriceFactorCalculator.MinVwapBars"/>
/// bars) or stale (last bar more than
/// <see cref="ShortSqueezePriceFactorCalculator.MaxStaleCalendarDays"/> days behind
/// the universe's latest price date).
/// </summary>
public class ShortSqueezePriceFactorCalculatorVwapTests
{
    [Fact]
    public void Compute_FlatSeries_PriceSitsOnTheVwap()
    {
        var bars = Series(count: 65, close: _ => 100m, volume: _ => 1_000);

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, bars[^1].Date);

        factors.PriceAboveVwap.Should().Be(0m);
    }

    [Fact]
    public void Compute_LastCloseAboveTheVolumeWeightedAverage_ProxyIsPositive()
    {
        // Flat at 100 for the whole window except a final bar at 130 — the VWAP sits
        // barely above 100, so the proxy must be close to +30%.
        var bars = Series(count: 65, close: i => i == 64 ? 130m : 100m, volume: _ => 1_000);

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, bars[^1].Date);

        factors.PriceAboveVwap.Should().BeGreaterThan(0.25m);
    }

    [Fact]
    public void Compute_HighVolumeDaysDominateTheAverage_ProxyIsVolumeWeighted()
    {
        // Half the window at 100 on heavy volume, half at 200 on negligible volume:
        // a simple mean sits near 150, but the VOLUME-weighted average stays near
        // 100 — so a last close of 200 must read as ~+100%, not ~+33%.
        var bars = Series(
            count: 64,
            close: i => i < 32 ? 100m : 200m,
            volume: i => i < 32 ? 1_000_000 : 1
        );

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, bars[^1].Date);

        factors.PriceAboveVwap.Should().BeGreaterThan(0.9m);
    }

    [Fact]
    public void Compute_TooFewBars_ProxyIsNull()
    {
        var bars = Series(
            count: ShortSqueezePriceFactorCalculator.MinVwapBars - 1,
            close: _ => 100m,
            volume: _ => 1_000
        );

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, bars[^1].Date);

        factors.PriceAboveVwap.Should().BeNull();
    }

    [Fact]
    public void Compute_StaleSeries_ProducesNoFactorsAtAll()
    {
        // A series that stopped trading (halt / delisting) would otherwise score on
        // frozen history; a last bar older than the staleness bound must disable
        // every price factor.
        var bars = Series(count: 65, close: i => i == 64 ? 130m : 100m, volume: _ => 1_000);
        var universeLatestDate = bars[^1]
            .Date.AddDays(ShortSqueezePriceFactorCalculator.MaxStaleCalendarDays + 1);

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, universeLatestDate);

        factors.PriceAboveVwap.Should().BeNull();
        factors.HasPriceSpikeCatalyst.Should().BeFalse();
        factors.HasVolumeSurgeCatalyst.Should().BeFalse();
    }

    [Fact]
    public void Compute_EmptySeries_ProducesNoFactors()
    {
        var factors = ShortSqueezePriceFactorCalculator.Compute([], new DateOnly(2026, 7, 1));

        factors.PriceAboveVwap.Should().BeNull();
        factors.HasPriceSpikeCatalyst.Should().BeFalse();
        factors.HasVolumeSurgeCatalyst.Should().BeFalse();
    }

    // Consecutive daily bars ending 2026-07-01; adjusted and raw close are equal so
    // the tests read naturally.
    private static List<ShortSqueezeDailyBar> Series(
        int count,
        Func<int, decimal> close,
        Func<int, long> volume
    )
    {
        var end = new DateOnly(2026, 7, 1);
        return Enumerable
            .Range(0, count)
            .Select(i => new ShortSqueezeDailyBar(
                end.AddDays(i - count + 1),
                close(i),
                close(i),
                volume(i)
            ))
            .ToList();
    }
}
