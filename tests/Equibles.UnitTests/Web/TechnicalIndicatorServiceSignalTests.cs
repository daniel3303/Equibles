using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceSignalTests
{
    #region DetectMaCross

    [Fact]
    public void DetectMaCross_MismatchedLengths_Throws()
    {
        var act = () => TechnicalIndicatorService.DetectMaCross([1m, 2m], [1m]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DetectMaCross_EmptySeries_ReturnsNone()
    {
        var result = TechnicalIndicatorService.DetectMaCross([], []);

        result.Should().Be(MovingAverageCrossSignal.None);
    }

    [Fact]
    public void DetectMaCross_SingleBar_ReturnsNone()
    {
        // No prior bar to compare against.
        var result = TechnicalIndicatorService.DetectMaCross([10m], [12m]);

        result.Should().Be(MovingAverageCrossSignal.None);
    }

    [Fact]
    public void DetectMaCross_ShortRisesAboveLong_ReturnsGoldenCross()
    {
        // Prior bar: short below long; latest bar: short above long.
        List<decimal?> shortMa = [9m, 11m];
        List<decimal?> longMa = [10m, 10m];

        var result = TechnicalIndicatorService.DetectMaCross(shortMa, longMa);

        result.Should().Be(MovingAverageCrossSignal.GoldenCross);
    }

    [Fact]
    public void DetectMaCross_ShortFallsBelowLong_ReturnsDeathCross()
    {
        List<decimal?> shortMa = [11m, 9m];
        List<decimal?> longMa = [10m, 10m];

        var result = TechnicalIndicatorService.DetectMaCross(shortMa, longMa);

        result.Should().Be(MovingAverageCrossSignal.DeathCross);
    }

    [Fact]
    public void DetectMaCross_PriorBarEqual_ThenAbove_ReturnsGoldenCross()
    {
        // Equality on the prior bar still counts as a cross when the next bar separates.
        List<decimal?> shortMa = [10m, 11m];
        List<decimal?> longMa = [10m, 10m];

        var result = TechnicalIndicatorService.DetectMaCross(shortMa, longMa);

        result.Should().Be(MovingAverageCrossSignal.GoldenCross);
    }

    [Fact]
    public void DetectMaCross_NoCross_ParallelSeries_ReturnsNone()
    {
        List<decimal?> shortMa = [11m, 12m, 13m];
        List<decimal?> longMa = [10m, 10m, 10m];

        var result = TechnicalIndicatorService.DetectMaCross(shortMa, longMa);

        result.Should().Be(MovingAverageCrossSignal.None);
    }

    [Fact]
    public void DetectMaCross_CrossOutsideLookbackWindow_ReturnsNone()
    {
        // Golden cross happens at index 1, but lookback only inspects the last 2
        // bar-pairs (indices 4-5 and 3-4), so it is not reported.
        List<decimal?> shortMa = [9m, 11m, 12m, 13m, 14m, 15m];
        List<decimal?> longMa = [10m, 10m, 10m, 10m, 10m, 10m];

        var result = TechnicalIndicatorService.DetectMaCross(shortMa, longMa, lookback: 2);

        result.Should().Be(MovingAverageCrossSignal.None);
    }

    [Fact]
    public void DetectMaCross_MostRecentCrossWins()
    {
        // Golden cross at index 1, death cross at index 3 — the latest is reported.
        List<decimal?> shortMa = [9m, 11m, 11m, 9m];
        List<decimal?> longMa = [10m, 10m, 10m, 10m];

        var result = TechnicalIndicatorService.DetectMaCross(shortMa, longMa, lookback: 5);

        result.Should().Be(MovingAverageCrossSignal.DeathCross);
    }

    [Fact]
    public void DetectMaCross_WarmupNullsSkipped_DetectsLaterCross()
    {
        // Leading nulls (SMA warm-up) must not be treated as a comparable bar.
        List<decimal?> shortMa = [null, null, 9m, 11m];
        List<decimal?> longMa = [null, null, 10m, 10m];

        var result = TechnicalIndicatorService.DetectMaCross(shortMa, longMa);

        result.Should().Be(MovingAverageCrossSignal.GoldenCross);
    }

    #endregion

    #region CountConsecutiveStreak

    [Fact]
    public void CountConsecutiveStreak_EmptySeries_ReturnsNone()
    {
        var (days, direction) = TechnicalIndicatorService.CountConsecutiveStreak([]);

        days.Should().Be(0);
        direction.Should().Be(PriceStreakDirection.None);
    }

    [Fact]
    public void CountConsecutiveStreak_SinglePrice_ReturnsNone()
    {
        var (days, direction) = TechnicalIndicatorService.CountConsecutiveStreak([100m]);

        days.Should().Be(0);
        direction.Should().Be(PriceStreakDirection.None);
    }

    [Fact]
    public void CountConsecutiveStreak_LastMoveFlat_ReturnsNone()
    {
        var (days, direction) = TechnicalIndicatorService.CountConsecutiveStreak([100m, 100m]);

        days.Should().Be(0);
        direction.Should().Be(PriceStreakDirection.None);
    }

    [Fact]
    public void CountConsecutiveStreak_ThreeRisingCloses_ReturnsThreeUp()
    {
        var (days, direction) = TechnicalIndicatorService.CountConsecutiveStreak([
            100m,
            101m,
            102m,
            103m,
        ]);

        days.Should().Be(3);
        direction.Should().Be(PriceStreakDirection.Up);
    }

    [Fact]
    public void CountConsecutiveStreak_ThreeFallingCloses_ReturnsThreeDown()
    {
        var (days, direction) = TechnicalIndicatorService.CountConsecutiveStreak([
            103m,
            102m,
            101m,
            100m,
        ]);

        days.Should().Be(3);
        direction.Should().Be(PriceStreakDirection.Down);
    }

    [Fact]
    public void CountConsecutiveStreak_DirectionChange_CountsOnlyTrailingRun()
    {
        // Down, down, then two up days at the end — only the trailing up run counts.
        var (days, direction) = TechnicalIndicatorService.CountConsecutiveStreak([
            105m,
            104m,
            103m,
            104m,
            105m,
        ]);

        days.Should().Be(2);
        direction.Should().Be(PriceStreakDirection.Up);
    }

    [Fact]
    public void CountConsecutiveStreak_FlatDayBreaksRun()
    {
        // The flat move between the two latest-but-one closes stops the count.
        var (days, direction) = TechnicalIndicatorService.CountConsecutiveStreak([
            100m,
            100m,
            101m,
            102m,
        ]);

        days.Should().Be(2);
        direction.Should().Be(PriceStreakDirection.Up);
    }

    #endregion
}
