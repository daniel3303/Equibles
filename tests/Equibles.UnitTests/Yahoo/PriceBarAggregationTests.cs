using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Data.Prices;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the weekly and monthly roll-up: first open, highest high, lowest low, last close and
/// adjusted close, summed volume, dated by the last session, and Partial only when the requested
/// window cuts a period's weekdays.
/// </summary>
public class PriceBarAggregationTests
{
    // A today after every period in a test, so only the window decides Partial there.
    private static readonly DateOnly Settled = new(2027, 6, 1);

    private static EquityDailyStockPrice Bar(
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume = 100
    ) =>
        new()
        {
            Date = date,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            AdjustedClose = close - 1m,
            Volume = volume,
        };

    [Theory]
    [InlineData(null, PriceBarInterval.Daily)]
    [InlineData("daily", PriceBarInterval.Daily)]
    [InlineData(" Weekly ", PriceBarInterval.Weekly)]
    [InlineData("1mo", PriceBarInterval.Monthly)]
    [InlineData("MONTHLY", PriceBarInterval.Monthly)]
    public void TryParse_AcceptsTheThreeIntervals(string value, PriceBarInterval expected)
    {
        PriceBarAggregation.TryParse(value, out var interval).Should().BeTrue();
        interval.Should().Be(expected);
    }

    [Theory]
    [InlineData("hourly")]
    [InlineData("quarterly")]
    public void TryParse_RefusesOtherIntervals(string value)
    {
        PriceBarAggregation.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Weekly_RollsUpEachIsoWeekDatedByItsLastSession()
    {
        // Monday 2026-10-12 to Friday 2026-10-16, then Monday 2026-10-19 alone.
        var daily = new[]
        {
            Bar(new DateOnly(2026, 10, 16), 12m, 13m, 11m, 12.5m, 300),
            Bar(new DateOnly(2026, 10, 12), 10m, 11m, 9m, 10.5m, 100),
            Bar(new DateOnly(2026, 10, 14), 10.5m, 14m, 10m, 12m, 200),
            Bar(new DateOnly(2026, 10, 19), 13m, 13.5m, 12m, 13m, 50),
        };

        var bars = PriceBarAggregation.Aggregate(
            daily,
            PriceBarInterval.Weekly,
            new DateOnly(2026, 10, 12),
            new DateOnly(2026, 10, 19),
            Settled
        );

        bars.Should().HaveCount(2);
        bars[0]
            .Should()
            .Be(
                new PriceBar(
                    new DateOnly(2026, 10, 16),
                    new DateOnly(2026, 10, 12),
                    10m,
                    14m,
                    9m,
                    12.5m,
                    11.5m,
                    600,
                    false
                )
            );
        bars[1].Date.Should().Be(new DateOnly(2026, 10, 19));
        bars[1].PeriodStart.Should().Be(new DateOnly(2026, 10, 19));
        bars[1].Partial.Should().BeTrue("the window ends on the Monday of an open week");
    }

    [Fact]
    public void Weekly_IsoWeekSpanningNewYearIsOneBucket()
    {
        // 2026-12-31 (Thursday) and 2027-01-01 (Friday) both sit in ISO week 2026-W53.
        var daily = new[]
        {
            Bar(new DateOnly(2026, 12, 31), 10m, 11m, 9m, 10m),
            Bar(new DateOnly(2027, 1, 1), 10m, 12m, 10m, 11m),
        };

        var bars = PriceBarAggregation.Aggregate(
            daily,
            PriceBarInterval.Weekly,
            new DateOnly(2026, 12, 1),
            new DateOnly(2027, 1, 31),
            Settled
        );

        bars.Should().ContainSingle().Which.PeriodStart.Should().Be(new DateOnly(2026, 12, 28));
    }

    [Fact]
    public void Monthly_MarksOnlyTheMonthsTheWindowCuts()
    {
        var daily = new[]
        {
            Bar(new DateOnly(2026, 8, 20), 5m, 6m, 4m, 5m),
            Bar(new DateOnly(2026, 9, 1), 5m, 7m, 5m, 6m),
            Bar(new DateOnly(2026, 9, 30), 6m, 8m, 3m, 7m),
            Bar(new DateOnly(2026, 10, 2), 7m, 7m, 6m, 6.5m),
        };

        var bars = PriceBarAggregation.Aggregate(
            daily,
            PriceBarInterval.Monthly,
            new DateOnly(2026, 8, 15),
            new DateOnly(2026, 10, 2),
            Settled
        );

        bars.Select(bar => bar.PeriodStart)
            .Should()
            .Equal(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));
        bars.Select(bar => bar.Partial).Should().Equal(true, false, true);
        bars[1].High.Should().Be(8m);
        bars[1].Low.Should().Be(3m);
        bars[1].Close.Should().Be(7m);
    }

    [Fact]
    public void Monthly_WindowEndingOnTheLastWeekdayIsComplete()
    {
        // October 2026 ends on a Saturday, so a window ending Friday the 30th covers every session.
        var daily = new[] { Bar(new DateOnly(2026, 10, 30), 5m, 6m, 4m, 5m) };

        var bars = PriceBarAggregation.Aggregate(
            daily,
            PriceBarInterval.Monthly,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 30),
            Settled
        );

        bars.Should().ContainSingle().Which.Partial.Should().BeFalse();
    }

    [Fact]
    public void Weekly_CurrentWeekOnItsLastWeekday_IsPartialUntilItTrades()
    {
        // Friday 2026-10-23 before the close: Monday to Thursday are stored and the default end
        // is today, which reaches the week's last weekday without its session.
        var daily = new[]
        {
            Bar(new DateOnly(2026, 10, 19), 10m, 11m, 9m, 10m),
            Bar(new DateOnly(2026, 10, 22), 10m, 12m, 10m, 11m),
        };
        var friday = new DateOnly(2026, 10, 23);

        PriceBarAggregation
            .Aggregate(daily, PriceBarInterval.Weekly, new DateOnly(2026, 10, 19), friday, friday)
            .Should()
            .ContainSingle()
            .Which.Partial.Should()
            .BeTrue("the week is still trading");

        PriceBarAggregation
            .Aggregate(
                [.. daily, Bar(friday, 11m, 12m, 10m, 11.5m)],
                PriceBarInterval.Weekly,
                new DateOnly(2026, 10, 19),
                friday,
                friday.AddDays(1)
            )
            .Should()
            .ContainSingle()
            .Which.Partial.Should()
            .BeFalse("the week closed on Friday");
    }

    [Fact]
    public void Monthly_FutureEndDate_LeavesTheCurrentMonthPartial()
    {
        var daily = new[]
        {
            Bar(new DateOnly(2026, 9, 30), 5m, 6m, 4m, 5m),
            Bar(new DateOnly(2026, 10, 21), 6m, 7m, 5m, 6m),
        };

        var bars = PriceBarAggregation.Aggregate(
            daily,
            PriceBarInterval.Monthly,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 10, 22)
        );

        bars.Select(bar => bar.Partial).Should().Equal(false, true);
    }

    [Fact]
    public void Aggregate_NoRows_ReturnsNoBars()
    {
        PriceBarAggregation
            .Aggregate(
                [],
                PriceBarInterval.Weekly,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1),
                Settled
            )
            .Should()
            .BeEmpty();
    }
}
