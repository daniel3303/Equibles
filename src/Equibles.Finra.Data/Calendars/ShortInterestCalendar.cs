using Equibles.Core.Calendars;

namespace Equibles.Finra.Data.Calendars;

/// <summary>
/// FINRA's semi-monthly short interest reporting calendar, derived from the rule FINRA
/// publishes at
/// https://www.finra.org/filing-reporting/regulatory-filing-systems/short-interest.
///
/// Three rules generate every row of that page's table:
///   settlement  — the 15th and the last day of each month, rolled BACK to the prior
///                 trading day when it lands on a weekend or NYSE holiday;
///   due         — <see cref="DueTradingDaysAfterSettlement"/> trading days after settlement
///                 ("by 6 p.m. Eastern Time on the second business day after the reporting
///                 settlement date", per FINRA Rule 4560);
///   publication — <see cref="PublicationTradingDaysAfterDue"/> trading days after the due
///                 date, when FINRA disseminates the file.
///
/// Deriving beats scraping here on both accuracy and reach: FINRA's page is behind a
/// bot-blocker (a plain HTTP GET is answered 403), it only ever lists the current and next
/// year, and the derivation reproduces every published row of both those years exactly —
/// <c>ShortInterestCalendarTests</c> pins the output against FINRA's tables verbatim, so a
/// rule change on FINRA's side fails the build rather than silently skewing the dates.
/// </summary>
public static class ShortInterestCalendar
{
    /// <summary>FINRA Rule 4560: reports are due the second business day after settlement.</summary>
    private const int DueTradingDaysAfterSettlement = 2;

    /// <summary>FINRA disseminates the file five business days after the filing deadline.</summary>
    private const int PublicationTradingDaysAfterDue = 5;

    /// <summary>Positions are also measured mid-month, as of the 15th.</summary>
    private const int MidMonthDay = 15;

    /// <summary>
    /// The reporting cycle for <paramref name="settlementDate"/>. The caller is expected to pass
    /// a real FINRA settlement date (see <see cref="CyclesInYear"/>); any date is accepted so a
    /// stored settlement date can be dated without first being matched against the calendar.
    /// </summary>
    public static ShortInterestReportingCycle ForSettlementDate(DateOnly settlementDate)
    {
        var dueDate = UsMarketCalendar.AddTradingDays(
            settlementDate,
            DueTradingDaysAfterSettlement
        );
        var publicationDate = UsMarketCalendar.AddTradingDays(
            dueDate,
            PublicationTradingDaysAfterDue
        );
        return new ShortInterestReportingCycle(settlementDate, dueDate, publicationDate);
    }

    /// <summary>The 24 cycles whose settlement date falls in <paramref name="year"/>, oldest first.</summary>
    public static IEnumerable<ShortInterestReportingCycle> CyclesInYear(int year)
    {
        foreach (var settlementDate in SettlementDatesInYear(year))
            yield return ForSettlementDate(settlementDate);
    }

    /// <summary>
    /// The next <paramref name="count"/> cycles whose data has not been published yet as of
    /// <paramref name="asOf"/> — a cycle publishing today still counts, since the file lands
    /// in the evening. Oldest first.
    /// </summary>
    /// <param name="asOf">Today's date in US Eastern time (the calendar's own time zone).</param>
    /// <param name="count">How many cycles to return.</param>
    /// <param name="afterSettlementDate">
    /// When set, only cycles settling strictly after this date are returned. Pass the newest
    /// settlement date already stored so a cycle whose file has just landed drops off the list
    /// instead of being listed as still upcoming.
    /// </param>
    public static List<ShortInterestReportingCycle> Upcoming(
        DateOnly asOf,
        int count,
        DateOnly? afterSettlementDate = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var upcoming = new List<ShortInterestReportingCycle>(count);
        // A cycle publishes at most ~4 weeks after it settles, so the pending window never
        // reaches back further than the previous month; start there and walk forward. The
        // horizon is bounded by count, and a year yields 24 cycles, so this is a short scan.
        for (var year = asOf.AddMonths(-1).Year; upcoming.Count < count; year++)
        {
            upcoming.AddRange(
                CyclesInYear(year)
                    .Where(cycle =>
                        cycle.PublicationDate >= asOf
                        && (afterSettlementDate is not { } floor || cycle.SettlementDate > floor)
                    )
                    .Take(count - upcoming.Count)
            );
        }
        return upcoming;
    }

    /// <summary>
    /// The cycle FINRA publishes on <paramref name="date"/>, or null when nothing is scheduled
    /// to publish that day. Drives the worker's publication-evening poll.
    /// </summary>
    public static ShortInterestReportingCycle PublishingOn(DateOnly date)
    {
        // A publication date is at most a few weeks after its settlement date, so a cycle
        // publishing today settled either this year or — for the early-January dates that
        // carry the December 31 settlement — the previous one.
        return CyclesInYear(date.Year - 1)
            .Concat(CyclesInYear(date.Year))
            .FirstOrDefault(cycle => cycle.PublicationDate == date);
    }

    // The 15th and the last day of each month, each rolled back to the prior trading day.
    private static IEnumerable<DateOnly> SettlementDatesInYear(int year)
    {
        for (var month = 1; month <= 12; month++)
        {
            yield return UsMarketCalendar.PreviousOrSameTradingDay(
                new DateOnly(year, month, MidMonthDay)
            );
            yield return UsMarketCalendar.PreviousOrSameTradingDay(
                new DateOnly(year, month, DateTime.DaysInMonth(year, month))
            );
        }
    }
}
