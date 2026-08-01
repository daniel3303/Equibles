using Equibles.Core.Calendars;

namespace Equibles.UnitTests.Core.Calendars;

public class UsMarketCalendarTests
{
    // Builds a UTC instant from an Eastern wall-clock time, DST handled by the zone.
    private static DateTimeOffset Et(int year, int month, int day, int hour, int minute)
    {
        var unspecified = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, UsMarketCalendar.EasternTimeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    [Theory]
    // Weekends.
    [InlineData(2025, 3, 15, false)] // Saturday
    [InlineData(2025, 3, 16, false)] // Sunday
    // Ordinary trading days.
    [InlineData(2025, 3, 12, true)] // Wednesday
    [InlineData(2021, 12, 31, true)] // Fri Dec 31 2021 — NYSE open (New Year's Sat is NOT observed)
    // Federal holidays the NYSE still trades on.
    [InlineData(2024, 10, 14, true)] // Columbus Day — open
    [InlineData(2024, 11, 11, true)] // Veterans Day — open
    // Good Friday (no weekend shift).
    [InlineData(2024, 3, 29, false)] // Good Friday 2024 — closed
    [InlineData(2024, 4, 1, true)] // Easter Monday — open
    // Fixed-date holidays + weekend observance.
    [InlineData(2025, 1, 1, false)] // New Year's Day (Wed) — closed
    [InlineData(2023, 1, 2, false)] // New Year's 2023 falls Sun -> observed Mon Jan 2 — closed
    [InlineData(2026, 7, 3, false)] // July 4 2026 is Sat -> observed Fri Jul 3 — closed
    [InlineData(2021, 7, 5, false)] // July 4 2021 is Sun -> observed Mon Jul 5 — closed
    [InlineData(2021, 12, 24, false)] // Christmas 2021 is Sat -> observed Fri Dec 24 — closed
    // Juneteenth (observed from 2022 only).
    [InlineData(2021, 6, 18, true)] // pre-adoption Friday — open
    [InlineData(2022, 6, 20, false)] // Jun 19 2022 is Sun -> observed Mon Jun 20 — closed
    [InlineData(2023, 6, 19, false)] // Jun 19 2023 (Mon) — closed
    // Floating Monday/Thursday holidays.
    [InlineData(2025, 1, 20, false)] // MLK Day (3rd Mon Jan)
    [InlineData(2025, 2, 17, false)] // Washington's Birthday (3rd Mon Feb)
    [InlineData(2025, 5, 26, false)] // Memorial Day (last Mon May)
    [InlineData(2025, 9, 1, false)] // Labor Day (1st Mon Sep)
    [InlineData(2025, 11, 27, false)] // Thanksgiving (4th Thu Nov)
    public void IsTradingDay_KnownDates_MatchesNyseSchedule(
        int year,
        int month,
        int day,
        bool expectedOpen
    )
    {
        UsMarketCalendar.IsTradingDay(new DateOnly(year, month, day)).Should().Be(expectedOpen);
    }

    [Theory]
    // 23:30 ET on New Year's Eve is already Jan 1 in UTC, so a caller reading the UTC date
    // would be a day ahead of the market calendar.
    [InlineData(2025, 12, 31, 23, 30, "2025-12-31")]
    [InlineData(2026, 7, 15, 0, 30, "2026-07-15")]
    [InlineData(2026, 7, 15, 16, 30, "2026-07-15")]
    public void EasternDate_ReturnsTheEasternCalendarDate_NotTheUtcOne(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        string expected
    )
    {
        UsMarketCalendar
            .EasternDate(Et(year, month, day, hour, minute))
            .Should()
            .Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void ToEastern_SummerInstant_IsEdtMinusFour()
    {
        var et = UsMarketCalendar.ToEastern(
            new DateTimeOffset(2025, 7, 15, 20, 0, 0, TimeSpan.Zero)
        );
        et.TimeOfDay.Should().Be(TimeSpan.FromHours(16));
        et.Offset.Should().Be(TimeSpan.FromHours(-4));
    }

    [Fact]
    public void ToEastern_WinterInstant_IsEstMinusFive()
    {
        var et = UsMarketCalendar.ToEastern(
            new DateTimeOffset(2025, 1, 15, 21, 0, 0, TimeSpan.Zero)
        );
        et.TimeOfDay.Should().Be(TimeSpan.FromHours(16));
        et.Offset.Should().Be(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void TimeUntilNextWindowStart_BeforeStartOnTradingDay_TargetsSameDay()
    {
        AssertNextWindow(from: Et(2025, 3, 12, 10, 0), expected: new DateOnly(2025, 3, 12));
    }

    [Fact]
    public void TimeUntilNextWindowStart_AfterStartOnTradingDay_TargetsNextTradingDay()
    {
        AssertNextWindow(from: Et(2025, 3, 12, 18, 0), expected: new DateOnly(2025, 3, 13));
    }

    [Fact]
    public void TimeUntilNextWindowStart_FridayEvening_SkipsWeekendToMonday()
    {
        AssertNextWindow(from: Et(2025, 3, 14, 18, 0), expected: new DateOnly(2025, 3, 17));
    }

    [Fact]
    public void TimeUntilNextWindowStart_FridayBeforeMondayHoliday_SkipsToTuesday()
    {
        // MLK Day 2025 = Mon Jan 20, so the next open window after Fri Jan 17 is Tue Jan 21.
        AssertNextWindow(from: Et(2025, 1, 17, 18, 0), expected: new DateOnly(2025, 1, 21));
    }

    [Fact]
    public void TimeUntilNextWindowStart_WeekendDay_TargetsNextTradingDay()
    {
        AssertNextWindow(from: Et(2025, 3, 15, 12, 0), expected: new DateOnly(2025, 3, 17));
    }

    [Theory]
    // Mid-week: the day before.
    [InlineData(2025, 3, 12, 2025, 3, 11)] // Wed -> Tue
    // Monday steps back over the weekend, not one calendar day.
    [InlineData(2025, 3, 17, 2025, 3, 14)] // Mon -> Fri
    // A trading day after a holiday steps over the holiday too. These are the cases a
    // "within N calendar days" guard cannot express: the gap is 4 days and still adjacent.
    [InlineData(2025, 1, 21, 2025, 1, 17)] // Tue after MLK Mon -> prior Fri
    [InlineData(2025, 5, 27, 2025, 5, 23)] // Tue after Memorial Mon -> prior Fri
    [InlineData(2026, 7, 6, 2026, 7, 2)] // Mon after the observed Jul 3 close -> prior Thu
    // Good Friday and Thanksgiving: the weekday before is closed, so it is skipped.
    [InlineData(2024, 4, 1, 2024, 3, 28)] // Easter Mon -> Thu (Good Friday closed)
    [InlineData(2025, 11, 28, 2025, 11, 26)] // Fri after Thanksgiving -> Wed
    // A weekend date still resolves to the last session before it.
    [InlineData(2025, 3, 16, 2025, 3, 14)] // Sun -> Fri
    public void PreviousTradingDay_StepsBackOverWeekendsAndHolidays(
        int year,
        int month,
        int day,
        int expectedYear,
        int expectedMonth,
        int expectedDay
    )
    {
        UsMarketCalendar
            .PreviousTradingDay(new DateOnly(year, month, day))
            .Should()
            .Be(new DateOnly(expectedYear, expectedMonth, expectedDay));
    }

    [Fact]
    public void PreviousTradingDay_OnATradingDay_NeverReturnsTheDateItself()
    {
        // The distinction from PreviousOrSameTradingDay: a day change measured from the date
        // being reported would always be zero.
        var tradingDay = new DateOnly(2025, 3, 12);

        UsMarketCalendar.PreviousOrSameTradingDay(tradingDay).Should().Be(tradingDay);
        UsMarketCalendar.PreviousTradingDay(tradingDay).Should().BeBefore(tradingDay);
    }

    private static void AssertNextWindow(DateTimeOffset from, DateOnly expected)
    {
        var windowStart = TimeSpan.FromHours(16);
        var delta = UsMarketCalendar.TimeUntilNextWindowStart(from, windowStart);
        delta.Should().BeGreaterThan(TimeSpan.Zero);

        var landed = UsMarketCalendar.ToEastern(from + delta);
        DateOnly.FromDateTime(landed.DateTime).Should().Be(expected);
        landed.TimeOfDay.Should().Be(windowStart);
    }
}
