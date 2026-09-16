using Equibles.Yahoo.Data.Prices;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the write guards every daily-bar writer shares, so a second source cannot store a bar
/// the Yahoo lane would refuse or reconcile one on a different split basis.
/// </summary>
public class DailyBarGuardsTests
{
    private static readonly DateOnly Today = new(2026, 9, 16);

    [Fact]
    public void MaxPriceValue_IsTheStoredPrecisionCeiling()
    {
        DailyBarGuards.MaxPriceValue.Should().Be(99_999_999_999_999.9999m);
        DailyBarGuards.ExceedsPriceRange(DailyBarGuards.MaxPriceValue).Should().BeFalse();
        DailyBarGuards.ExceedsPriceRange(DailyBarGuards.MaxPriceValue + 0.0001m).Should().BeTrue();
        DailyBarGuards.ExceedsPriceRange(-DailyBarGuards.MaxPriceValue - 1m).Should().BeTrue();
    }

    [Theory]
    [InlineData(10, 12, 9, 11, true)]
    [InlineData(10, 10, 10, 10, true)]
    [InlineData(0, 12, 9, 11, false)]
    [InlineData(10, 12, 0, 11, false)]
    [InlineData(10, 9, 8, 8.5, false)]
    [InlineData(10, 12, 9, 13, false)]
    [InlineData(10, 12, 11, 11, false)]
    [InlineData(10, 12, 9, 8, false)]
    public void IsValidOhlc_RequiresPositivePricesBracketedByHighAndLow(
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        bool expected
    )
    {
        DailyBarGuards.IsValidOhlc(open, high, low, close).Should().Be(expected);
    }

    [Fact]
    public void IsValidCandle_AlsoRefusesNegativeVolumeAndOverflow()
    {
        DailyBarGuards.IsValidCandle(10, 12, 9, 11, 0).Should().BeTrue();
        DailyBarGuards.IsValidCandle(10, 12, 9, 11, -1).Should().BeFalse();
        DailyBarGuards
            .IsValidCandle(10, DailyBarGuards.MaxPriceValue + 1m, 9, 11, 100)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(100, 100.5, true)]
    [InlineData(100, 101, true)]
    [InlineData(100, 101.01, false)]
    [InlineData(100, 80, false)]
    [InlineData(80, 100, false)]
    [InlineData(0.0050, 0.0051, true)]
    [InlineData(0, 100, false)]
    [InlineData(100, 0, false)]
    [InlineData(-1, -1, false)]
    public void IsSameSplitBasis_AcceptsOnlyMinorRevisionsInEitherDirection(
        decimal stored,
        decimal fetched,
        bool expected
    )
    {
        DailyBarGuards.IsSameSplitBasis(stored, fetched).Should().Be(expected);
    }

    [Fact]
    public void IsVolumeUpgrade_AcceptsOnlyAHigherFigure()
    {
        DailyBarGuards.IsVolumeUpgrade(1_000, 1_001).Should().BeTrue();
        DailyBarGuards.IsVolumeUpgrade(1_000, 1_000).Should().BeFalse();
        DailyBarGuards.IsVolumeUpgrade(1_000, 999).Should().BeFalse();
    }

    [Fact]
    public void IsSettledDailyBar_AcceptsOnlyDatesBeforeToday()
    {
        DailyBarGuards.IsSettledDailyBar(Today.AddDays(-1), Today).Should().BeTrue();
        DailyBarGuards.IsSettledDailyBar(Today, Today).Should().BeFalse();
        DailyBarGuards.IsSettledDailyBar(Today.AddDays(1), Today).Should().BeFalse();
    }
}
