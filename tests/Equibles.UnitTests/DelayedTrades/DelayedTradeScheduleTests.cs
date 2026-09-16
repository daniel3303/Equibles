using Equibles.DelayedTrades.BusinessLogic.Configuration;
using Equibles.DelayedTrades.BusinessLogic.Schedule;
using Equibles.EquityMarkets.Data.Catalog;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>
/// Contract: the intraday poll runs on weekdays from five minutes before the open to thirty minutes
/// after the closing auction at the poll cadence, in the market's own clock across a DST change; the
/// settle runs hourly while the last completed weekday session is not settled, counting from the
/// settle window start; the recheck runs once a day three hours after the open.
/// </summary>
public class DelayedTradeScheduleTests
{
    private static readonly EquityMarket Lisbon = EquityMarketCatalog.TryGet("euronext-lisbon");
    private static readonly EquityMarket Paris = EquityMarketCatalog.TryGet("euronext-paris");
    private static readonly TimeZoneInfo LisbonZone = TimeZoneInfo.FindSystemTimeZoneById(
        "Europe/Lisbon"
    );
    private static readonly TimeZoneInfo ParisZone = TimeZoneInfo.FindSystemTimeZoneById(
        "Europe/Paris"
    );
    private static readonly DelayedTradeScraperOptions Options = new();

    private static DelayedTradePlan Plan(
        EquityMarket market,
        TimeZoneInfo zone,
        DateTime utc,
        DelayedTradeMarketState state = null
    ) =>
        DelayedTradeSchedule.Plan(
            market,
            zone,
            utc,
            Options,
            state
                ?? new DelayedTradeMarketState
                {
                    SettledThroughDate = new DateOnly(2030, 1, 1),
                    LastRecheckDate = DateOnly.FromDateTime(utc).AddDays(1),
                }
        );

    [Fact]
    public void EveryCatalogMarketZone_Resolves()
    {
        foreach (var market in EquityMarketCatalog.All)
            DelayedTradeClock.Zone(market).Id.Should().Be(market.TimeZoneId);
    }

    [Theory]
    [InlineData(2026, 9, 15, 6, 54, false)]
    [InlineData(2026, 9, 15, 6, 55, true)]
    [InlineData(2026, 9, 15, 12, 0, true)]
    [InlineData(2026, 9, 15, 16, 5, true)]
    [InlineData(2026, 9, 15, 16, 6, false)]
    [InlineData(2026, 9, 19, 12, 0, false)]
    public void Intraday_FollowsLisbonSummerHours(
        int y,
        int m,
        int d,
        int h,
        int min,
        bool expected
    )
    {
        // Lisbon in September is UTC+1: the window is 07:55 to 16:05 local, 06:55 to 15:05 UTC... plus the post-auction slack.
        Plan(Lisbon, LisbonZone, new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc))
            .Intraday.Should()
            .Be(expected);
    }

    [Fact]
    public void Intraday_ShiftsWithTheDstChange()
    {
        // 07:30 UTC is 08:30 local before 2026-10-25 (in the window) and 07:30 local after it (before the window).
        Plan(Lisbon, LisbonZone, new DateTime(2026, 10, 23, 7, 30, 0, DateTimeKind.Utc))
            .Intraday.Should()
            .BeTrue();
        Plan(Lisbon, LisbonZone, new DateTime(2026, 10, 26, 7, 30, 0, DateTimeKind.Utc))
            .Intraday.Should()
            .BeFalse();
        Plan(Lisbon, LisbonZone, new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc))
            .Intraday.Should()
            .BeTrue();
    }

    [Fact]
    public void Intraday_RespectsThePollCadence()
    {
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var state = new DelayedTradeMarketState
        {
            SettledThroughDate = new DateOnly(2030, 1, 1),
            LastRecheckDate = new DateOnly(2026, 9, 15),
        };
        state.LastIntradayPollUtc = now.AddMinutes(-14);
        Plan(Paris, ParisZone, now, state).Intraday.Should().BeFalse();
        state.LastIntradayPollUtc = now.AddMinutes(-15);
        Plan(Paris, ParisZone, now, state).Intraday.Should().BeTrue();
    }

    [Theory]
    [InlineData(2026, 9, 15, 0, 10, 2026, 9, 11)]
    [InlineData(2026, 9, 15, 0, 30, 2026, 9, 14)]
    [InlineData(2026, 9, 15, 23, 0, 2026, 9, 14)]
    [InlineData(2026, 9, 12, 9, 0, 2026, 9, 11)]
    [InlineData(2026, 9, 13, 9, 0, 2026, 9, 11)]
    [InlineData(2026, 9, 14, 9, 0, 2026, 9, 11)]
    public void SettleTarget_IsTheLastWeekdayTheVenueHasFlippedTo(
        int y,
        int m,
        int d,
        int h,
        int min,
        int ty,
        int tm,
        int td
    )
    {
        DelayedTradeSchedule
            .SettleTarget(new DateTime(y, m, d, h, min, 0), Options)
            .Should()
            .Be(new DateOnly(ty, tm, td));
    }

    [Fact]
    public void Settle_RunsHourlyUntilTheTargetIsSettled()
    {
        var now = new DateTime(2026, 9, 15, 2, 0, 0, DateTimeKind.Utc);
        var state = new DelayedTradeMarketState { LastRecheckDate = new DateOnly(2026, 9, 15) };
        Plan(Paris, ParisZone, now, state).Settle.Should().BeTrue("a fresh process knows nothing");
        state.LastSettleAttemptUtc = now.AddMinutes(-30);
        Plan(Paris, ParisZone, now, state).Settle.Should().BeFalse();
        state.LastSettleAttemptUtc = now.AddMinutes(-60);
        Plan(Paris, ParisZone, now, state).Settle.Should().BeTrue();
        state.SettledThroughDate = new DateOnly(2026, 9, 14);
        Plan(Paris, ParisZone, now, state)
            .Settle.Should()
            .BeFalse("Monday's session is settled and Tuesday's has not flipped");
    }

    [Fact]
    public void Recheck_RunsOnceADay_ThreeHoursAfterTheOpen()
    {
        var state = new DelayedTradeMarketState { SettledThroughDate = new DateOnly(2030, 1, 1) };
        Plan(Paris, ParisZone, new DateTime(2026, 9, 15, 9, 59, 0, DateTimeKind.Utc), state)
            .Recheck.Should()
            .BeFalse("11:59 Paris");
        Plan(Paris, ParisZone, new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), state)
            .Recheck.Should()
            .BeTrue("12:00 Paris");
        state.LastRecheckDate = new DateOnly(2026, 9, 15);
        Plan(Paris, ParisZone, new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), state)
            .Recheck.Should()
            .BeFalse();
        Plan(Paris, ParisZone, new DateTime(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc), state)
            .Recheck.Should()
            .BeFalse("Saturday");
    }

    [Fact]
    public void Recheck_CanBeDisabled()
    {
        var options = new DelayedTradeScraperOptions { RecheckMinutesAfterOpen = 0 };
        var state = new DelayedTradeMarketState { SettledThroughDate = new DateOnly(2030, 1, 1) };
        DelayedTradeSchedule
            .Plan(
                Paris,
                ParisZone,
                new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
                options,
                state
            )
            .Recheck.Should()
            .BeFalse();
    }
}
