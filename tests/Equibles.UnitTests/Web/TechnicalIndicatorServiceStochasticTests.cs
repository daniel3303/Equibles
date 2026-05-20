using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceStochasticTests
{
    [Fact]
    public void ComputeStochastic_FirstKPeriodMinusOneBars_AreNull()
    {
        // 5-bar lookback → indices 0..3 are null while the window fills; index 4 is the
        // first %K value. %D needs an additional (dPeriod - 1) bars, so for k=5/d=3 the
        // first %D shows up at index 6.
        var highs = new List<decimal> { 10, 11, 12, 13, 14, 15, 16, 17 };
        var lows = new List<decimal> { 8, 9, 10, 11, 12, 13, 14, 15 };
        var closes = new List<decimal> { 9, 10, 11, 12, 13, 14, 15, 16 };

        var (k, d) = TechnicalIndicatorService.ComputeStochastic(highs, lows, closes, 5, 3);

        k.Should().HaveCount(8);
        d.Should().HaveCount(8);
        for (var i = 0; i < 4; i++)
            k[i].Should().BeNull($"index {i} is inside the %K warm-up");
        k[4].Should().NotBeNull();
        for (var i = 0; i < 6; i++)
            d[i].Should().BeNull($"index {i} is inside the %D warm-up (kPeriod-1 + dPeriod-1)");
        d[6].Should().NotBeNull();
    }

    [Fact]
    public void ComputeStochastic_HappyPath_ProducesExpectedPercentKAndD()
    {
        // Hand-computed fixture for k=3, d=3 across 5 bars.
        // Bars indexed 0..4 with H/L/C below:
        //  i=0: H=10 L=5  C=8
        //  i=1: H=12 L=6  C=11
        //  i=2: H=15 L=7  C=14   → window [0..2]: highestHigh=15 lowestLow=5 close=14 → %K = 100*(14-5)/10 = 90
        //  i=3: H=14 L=8  C=12   → window [1..3]: highestHigh=15 lowestLow=6 close=12 → %K = 100*(12-6)/9 = 66.6667
        //  i=4: H=16 L=9  C=15   → window [2..4]: highestHigh=16 lowestLow=7 close=15 → %K = 100*(15-7)/9 = 88.8889
        // %D at i=4 = average of %K at 2,3,4 = (90 + 66.6667 + 88.8889)/3 ≈ 81.8519
        var highs = new List<decimal> { 10, 12, 15, 14, 16 };
        var lows = new List<decimal> { 5, 6, 7, 8, 9 };
        var closes = new List<decimal> { 8, 11, 14, 12, 15 };

        var (k, d) = TechnicalIndicatorService.ComputeStochastic(highs, lows, closes, 3, 3);

        k[2].Should().Be(90m);
        k[3].Should().BeApproximately(66.6667m, 0.001m);
        k[4].Should().BeApproximately(88.8889m, 0.001m);
        d[4].Should().BeApproximately(81.8519m, 0.001m);
    }

    [Fact]
    public void ComputeStochastic_FlatRange_SetsPercentKToFiftyAsNeutralMidpoint()
    {
        // All bars share the same H/L → range = 0. Without a guard the formula divides
        // by zero; the documented convention is to emit 50 (neutral midpoint).
        var highs = new List<decimal> { 10, 10, 10 };
        var lows = new List<decimal> { 10, 10, 10 };
        var closes = new List<decimal> { 10, 10, 10 };

        var (k, _) = TechnicalIndicatorService.ComputeStochastic(highs, lows, closes, 3, 3);

        k[2].Should().Be(50m);
    }

    [Fact]
    public void ComputeStochastic_DefaultPeriods_AreFourteenAndThree()
    {
        // 17 bars of strictly increasing prices: %K at the end should land at 100 (close
        // is the lookback high). %D needs (14 - 1) + (3 - 1) = 15 warm-up bars before
        // its first value at index 15.
        var highs = Enumerable.Range(1, 17).Select(i => (decimal)i).ToList();
        var lows = highs.Select(h => h - 0.5m).ToList();
        var closes = highs;

        var (k, d) = TechnicalIndicatorService.ComputeStochastic(highs, lows, closes);

        k[16].Should().Be(100m);
        d[15].Should().NotBeNull();
        d[16].Should().NotBeNull();
    }

    [Fact]
    public void ComputeStochastic_MismatchedSeriesLengths_Throws()
    {
        var highs = new List<decimal> { 1, 2, 3 };
        var lows = new List<decimal> { 1, 2 };
        var closes = new List<decimal> { 1, 2, 3 };

        var act = () => TechnicalIndicatorService.ComputeStochastic(highs, lows, closes);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeStochastic_EmptyInput_ReturnsEmptyLists()
    {
        var (k, d) = TechnicalIndicatorService.ComputeStochastic([], [], []);

        k.Should().BeEmpty();
        d.Should().BeEmpty();
    }
}
