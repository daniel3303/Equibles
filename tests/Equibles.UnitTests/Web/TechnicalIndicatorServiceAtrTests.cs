using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceAtrTests
{
    [Fact]
    public void ComputeAtr_FirstPeriodMinusOneBars_AreNull()
    {
        // 3-period ATR over 5 bars: indices 0..1 are warm-up, index 2 carries the seed.
        var highs = new List<decimal> { 11, 12, 13, 14, 15 };
        var lows = new List<decimal> { 9, 10, 11, 12, 13 };
        var closes = new List<decimal> { 10, 11, 12, 13, 14 };

        var atr = TechnicalIndicatorService.ComputeAtr(highs, lows, closes, 3);

        atr.Should().HaveCount(5);
        atr[0].Should().BeNull();
        atr[1].Should().BeNull();
        atr[2].Should().NotBeNull();
    }

    [Fact]
    public void ComputeAtr_HappyPath_SeedIsAverageOfFirstPeriodTrueRanges()
    {
        // Bars chosen so TR is non-trivial at every step.
        // i=0: H=10 L=8  C=9   → TR_0 = H-L = 2
        // i=1: H=12 L=9  C=11  → prev_close=9 → TR_1 = max(12-9, |12-9|, |9-9|) = 3
        // i=2: H=14 L=10 C=13  → prev_close=11 → TR_2 = max(14-10, |14-11|, |10-11|) = 4
        // seed at i=2 = mean(2,3,4) = 3
        // i=3: H=13 L=8  C=9   → prev_close=13 → TR_3 = max(13-8, |13-13|, |8-13|) = 5
        // ATR_3 = (3 * 2 + 5) / 3 = 11/3 ≈ 3.6667
        // i=4: H=11 L=7  C=10  → prev_close=9 → TR_4 = max(11-7, |11-9|, |7-9|) = 4
        // ATR_4 = (3.6667 * 2 + 4) / 3 ≈ 3.7778
        var highs = new List<decimal> { 10, 12, 14, 13, 11 };
        var lows = new List<decimal> { 8, 9, 10, 8, 7 };
        var closes = new List<decimal> { 9, 11, 13, 9, 10 };

        var atr = TechnicalIndicatorService.ComputeAtr(highs, lows, closes, 3);

        atr[2].Should().Be(3m);
        atr[3].Should().BeApproximately(3.6667m, 0.001m);
        atr[4].Should().BeApproximately(3.7778m, 0.001m);
    }

    [Fact]
    public void ComputeAtr_FlatOhlc_ReturnsZeroVolatility()
    {
        // Identical bars → TR is always 0 → ATR is 0 throughout (post warm-up).
        var highs = new List<decimal> { 10, 10, 10, 10, 10 };
        var lows = new List<decimal> { 10, 10, 10, 10, 10 };
        var closes = new List<decimal> { 10, 10, 10, 10, 10 };

        var atr = TechnicalIndicatorService.ComputeAtr(highs, lows, closes, 3);

        atr[2].Should().Be(0m);
        atr[3].Should().Be(0m);
        atr[4].Should().Be(0m);
    }

    [Fact]
    public void ComputeAtr_ShorterThanPeriod_AllNulls()
    {
        // Only 5 bars but period 14 — the seed never lands, so every value stays null.
        var highs = Enumerable.Range(1, 5).Select(i => (decimal)(i + 1)).ToList();
        var lows = Enumerable.Range(1, 5).Select(i => (decimal)i).ToList();
        var closes = Enumerable.Range(1, 5).Select(i => (decimal)i).ToList();

        var atr = TechnicalIndicatorService.ComputeAtr(highs, lows, closes);

        atr.Should().HaveCount(5);
        atr.Should().OnlyContain(v => v == null);
    }

    [Fact]
    public void ComputeAtr_DefaultPeriodIsFourteen_SeedAtIndexThirteen()
    {
        // 20 bars with identical H/L/C across the series → TR is 0 every bar, ATR is 0
        // throughout. The point of this test is the default-period warm-up boundary:
        // index 12 must still be null, index 13 carries the first ATR value.
        var highs = Enumerable.Repeat(10m, 20).ToList();
        var lows = Enumerable.Repeat(10m, 20).ToList();
        var closes = Enumerable.Repeat(10m, 20).ToList();

        var atr = TechnicalIndicatorService.ComputeAtr(highs, lows, closes);

        atr[12].Should().BeNull();
        atr[13].Should().NotBeNull();
        atr[19].Should().Be(0m);
    }

    [Fact]
    public void ComputeAtr_MismatchedSeriesLengths_Throws()
    {
        var highs = new List<decimal> { 1, 2, 3 };
        var lows = new List<decimal> { 1, 2 };
        var closes = new List<decimal> { 1, 2, 3 };

        var act = () => TechnicalIndicatorService.ComputeAtr(highs, lows, closes);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeAtr_EmptyInput_ReturnsEmptyList()
    {
        var atr = TechnicalIndicatorService.ComputeAtr([], [], []);

        atr.Should().BeEmpty();
    }
}
