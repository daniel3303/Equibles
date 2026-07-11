using Equibles.Finra.BusinessLogic;
using Equibles.Finra.BusinessLogic.Models;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins the catalyst detection (ShortSqueezePriceFactorCalculator): the price
/// spike fires when the recent-window return is positive and a
/// <see cref="ShortSqueezePriceFactorCalculator.PriceSpikeSigmaMultiple"/>-sigma
/// outlier versus the stock's own baseline volatility; the volume surge fires when
/// recent dollar turnover runs
/// <see cref="ShortSqueezePriceFactorCalculator.VolumeSurgeMultiple"/>× the
/// baseline while the price moves against the shorts. Neither fires on a negative
/// move (that is covering, not a squeeze trigger) or on a baseline too short to
/// flag against.
/// </summary>
public class ShortSqueezePriceFactorCalculatorCatalystTests
{
    [Fact]
    public void Compute_ExtremePositiveWeek_FlagsThePriceSpike()
    {
        // Quiet ±0.5% baseline (daily sigma ≈ 0.5%, so the 5-bar threshold is ≈2.2%)
        // then a +30% final week on unchanged volume: a clear spike, but 1.3× dollar
        // turnover stays under the 2× surge bar.
        var bars = QuietBaselineThenFinalWeek(finalWeekClose: 130m, finalWeekVolume: 1_000);

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, bars[^1].Date);

        factors.HasPriceSpikeCatalyst.Should().BeTrue();
        factors.HasVolumeSurgeCatalyst.Should().BeFalse();
    }

    [Fact]
    public void Compute_TurnoverBurstOnAModestRise_FlagsTheVolumeSurgeOnly()
    {
        // A +1% week is inside the ±0.5%-sigma threshold (≈2.2%), but 4× the volume
        // at an unchanged price level is a clear dollar-turnover surge.
        var bars = QuietBaselineThenFinalWeek(finalWeekClose: 101m, finalWeekVolume: 4_000);

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, bars[^1].Date);

        factors.HasPriceSpikeCatalyst.Should().BeFalse();
        factors.HasVolumeSurgeCatalyst.Should().BeTrue();
    }

    [Fact]
    public void Compute_CrashOnHugeVolume_FlagsNothing()
    {
        // Price collapsing on heavy volume is shorts WINNING (or covering) — the
        // catalysts require the move to run against them.
        var bars = QuietBaselineThenFinalWeek(finalWeekClose: 60m, finalWeekVolume: 10_000);

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, bars[^1].Date);

        factors.HasPriceSpikeCatalyst.Should().BeFalse();
        factors.HasVolumeSurgeCatalyst.Should().BeFalse();
    }

    [Fact]
    public void Compute_BaselineTooShort_CatalystsStayOff()
    {
        // 25 baseline bars + the recent week is under the 30-bar minimum baseline:
        // the VWAP proxy still computes (≥ 20 bars) but no catalyst may fire on a
        // volatility estimate that thin.
        var bars = QuietBaselineThenFinalWeek(
            finalWeekClose: 130m,
            finalWeekVolume: 10_000,
            baselineBars: 25
        );

        var factors = ShortSqueezePriceFactorCalculator.Compute(bars, bars[^1].Date);

        factors.PriceAboveVwap.Should().NotBeNull();
        factors.HasPriceSpikeCatalyst.Should().BeFalse();
        factors.HasVolumeSurgeCatalyst.Should().BeFalse();
    }

    // A ±0.5% zig-zag around 100 on constant volume for the baseline, then five
    // final bars stepping linearly to `finalWeekClose` on `finalWeekVolume`.
    private static List<ShortSqueezeDailyBar> QuietBaselineThenFinalWeek(
        decimal finalWeekClose,
        long finalWeekVolume,
        int baselineBars = 61
    )
    {
        var end = new DateOnly(2026, 7, 1);
        var total = baselineBars + 5;
        var bars = new List<ShortSqueezeDailyBar>(total);
        for (var i = 0; i < baselineBars; i++)
        {
            var close = i % 2 == 0 ? 100m : 100.5m;
            bars.Add(new ShortSqueezeDailyBar(end.AddDays(i - total + 1), close, close, 1_000));
        }

        var start = bars[^1].AdjustedClose;
        for (var i = 0; i < 5; i++)
        {
            var close = start + (finalWeekClose - start) * (i + 1) / 5;
            bars.Add(
                new ShortSqueezeDailyBar(
                    end.AddDays(baselineBars + i - total + 1),
                    close,
                    close,
                    finalWeekVolume
                )
            );
        }

        return bars;
    }
}
