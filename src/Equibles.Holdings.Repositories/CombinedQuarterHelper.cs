using Equibles.Core.Calendars;

namespace Equibles.Holdings.Repositories;

// The one 13F filing-calendar rule (SEC rule 13f-1: filings are due 45 calendar days after each
// quarter end, moved to the next business day when that falls on a weekend or holiday). Every
// surface that decides how to present the newest quarter must go through this helper so "the
// current quarter" resolves identically everywhere: while the window is
// open the quarter only holds the funds that filed early, so it must be shown as the COMBINED
// view (current filings + prior-quarter carry-forward for funds yet to file) — never as a
// complete quarter.
public static class CombinedQuarterHelper
{
    public const int FilingDeadlineDays = 45;

    // SEC Rule 0-3 moves a deadline on a weekend or federal holiday to the next business day. The
    // NYSE calendar stands in for SEC business days: no 13F deadline roll (Feb 14-17, May 15-17,
    // Aug 14-16, Nov 14-16) can reach a day where the two calendars differ.
    public static DateOnly FilingDeadline(DateOnly reportDate)
    {
        return UsMarketCalendar.NextOrSameTradingDay(reportDate.AddDays(FilingDeadlineDays));
    }

    public static bool IsFilingWindowOpen(DateOnly latestReportDate)
    {
        return IsFilingWindowOpen(latestReportDate, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    // Explicit-today overload so callers and tests can pin the clock.
    public static bool IsFilingWindowOpen(DateOnly latestReportDate, DateOnly today)
    {
        return today <= FilingDeadline(latestReportDate);
    }

    // The latest ReportDate whose filing window has fully closed as of `today`: its deadline is
    // a business day before today, so deadline-day filings have been ingested. Quarter-over-quarter
    // verdicts and multi-year extremes must only anchor on dates at or before this cutoff.
    public static DateOnly CompletedReportDateCutoff(DateOnly today)
    {
        return UsMarketCalendar.PreviousTradingDay(today).AddDays(-FilingDeadlineDays);
    }
}
