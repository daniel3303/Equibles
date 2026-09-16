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

    [Theory]
    [InlineData(15, 50, 16, 6, true)] // last poll 16:50 local, before the 16:51 completion time: one closing poll
    [InlineData(15, 52, 16, 7, false)] // last poll 16:52 local already saw the auction's aged prints
    [InlineData(16, 6, 16, 22, false)] // the closing poll itself was the last one
    [InlineData(15, 50, 16, 5, true)] // still inside the window, the cadence alone decides
    public void Intraday_GrantsOneClosingPoll_WhenTheLastOneRanBeforeTheCompletionTime(
        int lastHour,
        int lastMinute,
        int nowHour,
        int nowMinute,
        bool expected
    )
    {
        // Lisbon in September: the window closes 17:05 local (16:05 UTC), the closing auction's prints have aged
        // past the delay at 16:51 local (15:51 UTC).
        var state = new DelayedTradeMarketState
        {
            SettledThroughDate = new DateOnly(2030, 1, 1),
            LastRecheckDate = new DateOnly(2026, 9, 16),
            LastIntradayPollUtc = new DateTime(
                2026,
                9,
                15,
                lastHour,
                lastMinute,
                0,
                DateTimeKind.Utc
            ),
        };
        Plan(
            Lisbon,
            LisbonZone,
            new DateTime(2026, 9, 15, nowHour, nowMinute, 0, DateTimeKind.Utc),
            state
        )
            .Intraday.Should()
            .Be(expected);
    }

    [Fact]
    public void ClosingPoll_NeverCrossesIntoTheNextDay()
    {
        var state = new DelayedTradeMarketState
        {
            SettledThroughDate = new DateOnly(2030, 1, 1),
            LastRecheckDate = new DateOnly(2026, 9, 17),
            LastIntradayPollUtc = new DateTime(2026, 9, 15, 15, 50, 0, DateTimeKind.Utc),
        };
        Plan(Lisbon, LisbonZone, new DateTime(2026, 9, 16, 5, 0, 0, DateTimeKind.Utc), state)
            .Intraday.Should()
            .BeFalse("yesterday's short poll is not today's closing poll");
    }

    [Fact]
    public void CompletionTime_IsTheAuctionEndPlusTheDelayAndAMinute()
    {
        DelayedTradeSchedule.CompletionTime(Lisbon, Options).Should().Be(new TimeOnly(16, 51));
        DelayedTradeSchedule.CompletionTime(Paris, Options).Should().Be(new TimeOnly(17, 51));
    }

    public static TheoryData<string, string, string, bool, string> SettledThroughCases =>
        new()
        {
            // The file carried the target: settled through it.
            { null, "2026-09-14", "2026-09-14", false, "2026-09-14" },
            { null, "2026-09-14", "2026-09-14", true, "2026-09-14" },
            // An older session with no intraday sighting of the target: a holiday, nothing to wait for.
            { null, "2026-09-11", "2026-09-14", false, "2026-09-14" },
            // An older session while the polls saw the target's prints: the file has not flipped, keep settling.
            { null, "2026-09-11", "2026-09-14", true, "2026-09-11" },
            // Never regresses what an earlier pass settled.
            { "2026-09-14", "2026-09-11", "2026-09-15", true, "2026-09-14" },
            { "2026-09-15", "2026-09-14", "2026-09-15", false, "2026-09-15" },
        };

    [Theory]
    [MemberData(nameof(SettledThroughCases))]
    public void SettledThrough_WaitsOnlyForASessionThePollsSaw(
        string current,
        string sessionDate,
        string target,
        bool targetSeenIntraday,
        string expected
    )
    {
        DelayedTradeSchedule
            .SettledThrough(
                current == null ? null : DateOnly.Parse(current),
                sessionDate == null ? null : DateOnly.Parse(sessionDate),
                DateOnly.Parse(target),
                targetSeenIntraday
            )
            .Should()
            .Be(DateOnly.Parse(expected));
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
