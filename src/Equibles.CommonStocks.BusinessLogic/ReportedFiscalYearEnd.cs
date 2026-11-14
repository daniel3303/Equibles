namespace Equibles.CommonStocks.BusinessLogic;

/// <summary>
/// The fiscal year end a registrant's own annual reports state. EDGAR's company record carries a
/// year end no report confirms for some registrants (a December filer recorded as January 19), so
/// two agreeing annual reports outrank it while they are current.
/// </summary>
public record ReportedFiscalYearEnd(int Month, int Day, DateOnly NewestAnnualPeriodEnd)
{
    // The next annual report is due within twelve months of the year end plus the filing window.
    public const int CurrentWithinDays = 485;

    private const int MinYearDays = 350;
    private const int MaxYearDays = 380;

    // The same quarter one year on closes 364 days later, 371 in a 53-week year.
    private const int YearOnYearDays = 364;
    private const int YearOnYearToleranceDays = 10;

    /// <summary>
    /// Null unless the two newest annual reports close ordinary years on the same month and no
    /// later quarterly report leaves that calendar: a transition report, a single annual report or
    /// a quarter on another grid each mean the year end may have changed, which only the company
    /// record states.
    /// </summary>
    public static ReportedFiscalYearEnd Read(
        IEnumerable<DateOnly> annualPeriodEnds,
        IEnumerable<DateOnly> quarterlyPeriodEnds
    )
    {
        var annualEnds = annualPeriodEnds.Distinct().OrderDescending().Take(2).ToList();
        if (annualEnds.Count < 2)
            return null;

        var newest = FiscalCalendar.YearEndOnMonthGrid(annualEnds[0].Month, annualEnds[0].Day);
        var previous = FiscalCalendar.YearEndOnMonthGrid(annualEnds[1].Month, annualEnds[1].Day);
        var yearDays = annualEnds[0].DayNumber - annualEnds[1].DayNumber;
        if (newest.Month != previous.Month || yearDays is < MinYearDays or > MaxYearDays)
            return null;

        if (LaterQuarterLeavesCalendar(annualEnds[0], quarterlyPeriodEnds))
            return null;

        return new ReportedFiscalYearEnd(newest.Month, newest.Day.Value, annualEnds[0]);
    }

    // A quarterly report after the newest annual one that closes off its calendar: the first sign
    // of a changed year end, ahead of any annual report on the new one.
    private static bool LaterQuarterLeavesCalendar(
        DateOnly newestAnnualEnd,
        IEnumerable<DateOnly> quarterlyPeriodEnds
    )
    {
        var yearEndMonth = FiscalCalendar
            .YearEndOnMonthGrid(newestAnnualEnd.Month, newestAnnualEnd.Day)
            .Month;
        var quarterEnds = quarterlyPeriodEnds.Distinct().ToList();
        return quarterEnds
            .Where(end => end > newestAnnualEnd)
            .Any(end => !FollowsCalendar(end, yearEndMonth, quarterEnds));
    }

    /// <summary>
    /// The year end to record from the company record's value and the registrant's listed report
    /// period ends; null leaves the recorded one alone.
    /// </summary>
    public static (int Month, int? Day)? Choose(
        int? recordMonth,
        int? recordDay,
        IReadOnlyCollection<DateOnly> annualPeriodEnds,
        IReadOnlyCollection<DateOnly> quarterlyPeriodEnds,
        int? recordedMonth,
        int? recordedDay,
        bool filesAnnualReportsNow,
        DateOnly today
    )
    {
        (int Month, int? Day)? record = recordMonth is { } month
            ? FiscalCalendar.RecordedYearEnd(month, recordDay)
            : null;
        var recordGridMonth = record is { } stated
            ? FiscalCalendar.YearEndOnMonthGrid(stated.Month, stated.Day).Month
            : (int?)null;
        var reported = Read(annualPeriodEnds, quarterlyPeriodEnds);
        if (reported != null && reported.Month != recordGridMonth)
        {
            if (today.DayNumber - reported.NewestAnnualPeriodEnd.DayNumber <= CurrentWithinDays)
                return (reported.Month, reported.Day);
            // A late filer's reports stop being current before its next one arrives; the value they
            // set must not fall back to the record meanwhile.
            if (recordedMonth != null)
                return null;
        }
        // Without two agreeing reports the record decides, unless the newest annual report still
        // closes on the recorded month, the record names another and no later quarter has left
        // that calendar: the reports set that month earlier, and a registrant that files heavily
        // lists a single annual report for part of the year, so falling back would move the year
        // end to and fro.
        if (
            reported == null
            && recordedMonth is { } held
            && annualPeriodEnds.Count > 0
            && annualPeriodEnds.Max() is var newest
            && FiscalCalendar.YearEndOnMonthGrid(newest.Month, newest.Day).Month is var reportMonth
            && reportMonth == FiscalCalendar.YearEndOnMonthGrid(held, recordedDay).Month
            && reportMonth != recordGridMonth
            && !LaterQuarterLeavesCalendar(newest, quarterlyPeriodEnds)
        )
            return null;
        // A filer without a current annual report (a Form D or F-1 filer, or one that left years
        // ago) states a year end no report of its confirms, so it only fills a blank.
        return record != null && (filesAnnualReportsNow || recordedMonth == null) ? record : null;
    }

    // A quarter belongs to the calendar when it closes on the year end's month grid, or one year
    // after a quarter already reported (12- and 16-week quarters sit off the month grid every year).
    private static bool FollowsCalendar(
        DateOnly quarterEnd,
        int yearEndMonth,
        IReadOnlyCollection<DateOnly> quarterEnds
    ) =>
        (FiscalCalendar.YearEndOnMonthGrid(quarterEnd.Month, quarterEnd.Day).Month - yearEndMonth)
            % 3
            == 0
        || quarterEnds.Any(earlier =>
            Math.Abs(quarterEnd.DayNumber - earlier.DayNumber - YearOnYearDays)
            <= YearOnYearToleranceDays
        );
}
