namespace Equibles.Holdings.Repositories;

// The one 13F filing-calendar rule (SEC rule 13f-1: filings are due 45 calendar days after each
// quarter end). Every surface that decides how to present the newest quarter must go through
// this helper so "the current quarter" resolves identically everywhere: while the window is
// open the quarter only holds the funds that filed early, so it must be shown as the COMBINED
// view (current filings + prior-quarter carry-forward for funds yet to file) — never as a
// complete quarter.
public static class CombinedQuarterHelper
{
    public const int FilingDeadlineDays = 45;

    public static bool IsFilingWindowOpen(DateOnly latestReportDate)
    {
        return IsFilingWindowOpen(latestReportDate, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    // Explicit-today overload so callers and tests can pin the clock.
    public static bool IsFilingWindowOpen(DateOnly latestReportDate, DateOnly today)
    {
        return today <= latestReportDate.AddDays(FilingDeadlineDays);
    }

    // The latest ReportDate whose filing window has fully closed as of `today` (the deadline
    // plus one day so deadline-day filings have been ingested). Quarter-over-quarter verdicts
    // and multi-year extremes must only anchor on dates at or before this cutoff.
    public static DateOnly CompletedReportDateCutoff(DateOnly today)
    {
        return today.AddDays(-(FilingDeadlineDays + 1));
    }
}
