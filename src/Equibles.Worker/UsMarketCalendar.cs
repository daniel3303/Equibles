namespace Equibles.Worker;

/// <summary>
/// NYSE trading-day calendar plus US Eastern time helpers. Pure and deterministic so the
/// FINRA evening poller can decide "is the market open today, and is it past the close"
/// without any external dependency or stored calendar. Models the regular full-day NYSE
/// holiday closures; early-close half-days (e.g. the day after Thanksgiving) remain trading
/// days because short-sale volume is still published for them.
/// </summary>
public static class UsMarketCalendar
{
    // IANA id; .NET resolves it cross-platform via ICU, so it works in the UTC Docker image.
    public static readonly TimeZoneInfo EasternTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        "America/New_York"
    );

    // NYSE began observing Juneteenth as a market holiday in 2022.
    private const int JuneteenthFirstObservedYear = 2022;

    public static DateTimeOffset ToEastern(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, EasternTimeZone);

    /// <summary>True for any weekday that is not an observed NYSE holiday.</summary>
    public static bool IsTradingDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        return !IsNyseHoliday(date);
    }

    public static bool IsNyseHoliday(DateOnly date)
    {
        var year = date.Year;

        // New Year's Day: observed the following Monday when it falls on a Sunday. When it
        // falls on a Saturday the NYSE does NOT close the preceding Friday (Dec 31 stays a
        // normal trading day) — this is the one holiday that breaks the Sat -> Fri rule.
        if (date == NewYearsObserved(year))
            return true;

        // Fixed-date holidays observed with the full weekend shift (Sat -> prior Fri,
        // Sun -> following Mon).
        if (year >= JuneteenthFirstObservedYear && date == ObservedDate(new DateOnly(year, 6, 19)))
            return true; // Juneteenth National Independence Day
        if (date == ObservedDate(new DateOnly(year, 7, 4)))
            return true; // Independence Day
        if (date == ObservedDate(new DateOnly(year, 12, 25)))
            return true; // Christmas Day

        // Floating Monday/Thursday holidays (never weekend-shifted).
        if (date == NthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 3))
            return true; // Martin Luther King Jr. Day
        if (date == NthWeekdayOfMonth(year, 2, DayOfWeek.Monday, 3))
            return true; // Washington's Birthday
        if (date == LastWeekdayOfMonth(year, 5, DayOfWeek.Monday))
            return true; // Memorial Day
        if (date == NthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1))
            return true; // Labor Day
        if (date == NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4))
            return true; // Thanksgiving Day

        // Good Friday — two days before Easter Sunday; always a Friday, no weekend shift.
        if (date == EasterSunday(year).AddDays(-2))
            return true;

        return false;
    }

    /// <summary>
    /// Time from <paramref name="now"/> until the next trading-day poll window start
    /// (<paramref name="windowStart"/> ET). Used as an idle-cycle cap so a long
    /// <c>SleepInterval</c> never sleeps past the next evening window. Floored at zero.
    /// </summary>
    public static TimeSpan TimeUntilNextWindowStart(DateTimeOffset now, TimeSpan windowStart)
    {
        var nowEt = ToEastern(now);
        var etDate = DateOnly.FromDateTime(nowEt.DateTime);

        DateOnly candidate;
        if (IsTradingDay(etDate) && nowEt.TimeOfDay < windowStart)
        {
            candidate = etDate;
        }
        else
        {
            candidate = etDate.AddDays(1);
            while (!IsTradingDay(candidate))
                candidate = candidate.AddDays(1);
        }

        var startEt = candidate.ToDateTime(
            TimeOnly.FromTimeSpan(windowStart),
            DateTimeKind.Unspecified
        );
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startEt, EasternTimeZone);
        var delta = startUtc - now.UtcDateTime;
        // The chosen candidate is always strictly in the future, so Zero is only a benign
        // sub-second-rounding degenerate (the caller treats it as "run again now").
        return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
    }

    private static DateOnly NewYearsObserved(int year)
    {
        var jan1 = new DateOnly(year, 1, 1);
        return jan1.DayOfWeek == DayOfWeek.Sunday ? jan1.AddDays(1) : jan1;
    }

    // Sat -> preceding Fri, Sun -> following Mon; otherwise the date itself.
    private static DateOnly ObservedDate(DateOnly fixedDate) =>
        fixedDate.DayOfWeek switch
        {
            DayOfWeek.Saturday => fixedDate.AddDays(-1),
            DayOfWeek.Sunday => fixedDate.AddDays(1),
            _ => fixedDate,
        };

    private static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek weekday, int n)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (n - 1));
    }

    private static DateOnly LastWeekdayOfMonth(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-offset);
    }

    // Anonymous Gregorian algorithm (Meeus/Jones/Butcher) for Easter Sunday.
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
