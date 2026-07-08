using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Derives a fact's (FiscalYear, FiscalPeriod) from the period it actually
/// measures, rather than from the filing's <c>fy</c>/<c>fp</c> identity. SEC
/// ships every value in a filing with the filing's own fiscal-year qualifier
/// (a FY2024 10-K stamps all three comparable years as fy=2024 / fp=FY), so
/// using those fields as period identity collapses distinct actual periods
/// into one row at the unique-index level — see issue #982.
/// </summary>
internal static class FiscalPeriodResolver
{
    // 52/53-week filers (Apple, Microsoft, etc.) slide PeriodEnd a few days
    // around the nominal fiscal year end. ±14 days is comfortably wider than
    // any real-world drift while still rejecting unrelated dates.
    private const int FyeMatchWindowDays = 14;

    // A fiscal-year end in the first days of January marks a 52/53-week filer
    // whose year-end oscillates around Dec 31 and occasionally lands just into
    // the new year (e.g. Johnson & Johnson: SEC's submissions fiscalYearEnd is
    // "0103", yet every recent fiscal year is December-anchored — FY2025 ended
    // 2025-12-28). It is *not* a genuine late-January retail year-end (Walmart,
    // Target: day ~31). The day cutoff separates the two.
    private const int EarlyJanuaryFiscalYearEndDay = 7;

    private const int AnnualMinDays = 350;
    private const int AnnualMaxDays = 380;
    private const int QuarterMinDays = 80;
    private const int QuarterMaxDays = 100;
    private const int HalfYearMinDays = 170;
    private const int HalfYearMaxDays = 190;
    private const int NineMonthMinDays = 260;
    private const int NineMonthMaxDays = 280;

    /// <summary>
    /// Resolves <paramref name="periodStart"/> / <paramref name="periodEnd"/>
    /// against the company's fiscal-year-end month and day. Returns
    /// <c>null</c> when the FYE is unknown or the period duration doesn't
    /// match any recognised shape — callers fall back to the filing-supplied
    /// identity in that case.
    /// <para>
    /// <paramref name="classifyInterimInstants"/> opts an instant that is NOT at the
    /// fiscal-year end into quarter classification (which fiscal quarter contains the
    /// date). Off by default: callers with an SEC-supplied fp rely on the null
    /// fallback there, and re-labelling their instants would rewrite fiscal
    /// identities corpus-wide. Only fp-less values (6-K interim balance sheets,
    /// which SEC serves with <c>fp = null</c>) opt in — for them the date is the
    /// only identity available.
    /// </para>
    /// </summary>
    public static (int Year, SecFiscalPeriod Period)? Resolve(
        DateOnly periodStart,
        DateOnly periodEnd,
        int? fyeMonth,
        int? fyeDay,
        bool classifyInterimInstants = false
    )
    {
        if (fyeMonth is null || fyeDay is null)
            return null;
        if (fyeMonth < 1 || fyeMonth > 12 || fyeDay < 1 || fyeDay > 31)
            return null;

        // Treat an early-January (year-turn) fiscal-year end as December-anchored
        // so the period lands in the year the company actually reports. Without
        // this, every period for such a filer is labelled one fiscal year too
        // high (e.g. J&J's quarter ending 2026-03-29 would resolve to FY2027
        // instead of FY2026). Issue #4423.
        if (fyeMonth == 1 && fyeDay <= EarlyJanuaryFiscalYearEndDay)
        {
            fyeMonth = 12;
            fyeDay = 31;
        }

        var candidates = new[]
        {
            CreateSafe(periodEnd.Year - 1, fyeMonth.Value, fyeDay.Value),
            CreateSafe(periodEnd.Year, fyeMonth.Value, fyeDay.Value),
            CreateSafe(periodEnd.Year + 1, fyeMonth.Value, fyeDay.Value),
        };

        var durationDays = periodEnd.DayNumber - periodStart.DayNumber;
        var isInstant = durationDays == 0;
        var isAnnual = IsWithinDays(durationDays, AnnualMinDays, AnnualMaxDays);

        if (isAnnual || isInstant)
        {
            var closest = ClosestTo(candidates, periodEnd);
            if (Math.Abs(closest.DayNumber - periodEnd.DayNumber) <= FyeMatchWindowDays)
                return (closest.Year, SecFiscalPeriod.FullYear);
            // An opted-in interim instant falls through to the quarter classification
            // below; everything else keeps the null fallback (see the summary).
            if (!isInstant || !classifyInterimInstants)
                return null;
        }

        var isQuarter = IsWithinDays(durationDays, QuarterMinDays, QuarterMaxDays);
        var isHalfYear = IsWithinDays(durationDays, HalfYearMinDays, HalfYearMaxDays);
        var isNineMonths = IsWithinDays(durationDays, NineMonthMinDays, NineMonthMaxDays);

        var interimInstant = isInstant && classifyInterimInstants;
        if (!interimInstant && !isQuarter && !isHalfYear && !isNineMonths)
            return null;

        // The fiscal year containing periodEnd is the one whose FYE is on or
        // after periodEnd within roughly twelve months — ranks the matched
        // year correctly even when the quarter closes a few days late.
        var matches = candidates.Where(c => c.DayNumber >= periodEnd.DayNumber).ToList();
        if (matches.Count == 0)
            return null;
        var endingFye = matches.MinBy(c => c.DayNumber);
        if (endingFye.DayNumber - periodEnd.DayNumber > AnnualMaxDays)
            return null;

        if (endingFye.Year < 2)
            return null;
        var priorFye = endingFye.AddYears(-1);
        var fiscalYearStart = priorFye.AddDays(1);
        var monthsElapsed =
            (periodEnd.Year - fiscalYearStart.Year) * 12
            + (periodEnd.Month - fiscalYearStart.Month);

        var period = monthsElapsed switch
        {
            <= 4 => SecFiscalPeriod.Q1,
            <= 7 => SecFiscalPeriod.Q2,
            <= 10 => SecFiscalPeriod.Q3,
            _ => SecFiscalPeriod.Q4,
        };

        return (endingFye.Year, period);
    }

    // Companies with a Feb 29 FYE land on Feb 28 in non-leap years; clamp the
    // requested day to the month's actual length so DateOnly construction
    // never throws.
    private static DateOnly CreateSafe(int year, int month, int day)
    {
        if (year < 1)
            return DateOnly.MinValue;
        if (year > 9999)
            return DateOnly.MaxValue;
        var maxDay = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, Math.Min(day, maxDay));
    }

    private static DateOnly ClosestTo(DateOnly[] candidates, DateOnly target) =>
        candidates.MinBy(c => Math.Abs(c.DayNumber - target.DayNumber));

    // Inclusive day-count band for a recognised period shape (annual, quarter, …).
    private static bool IsWithinDays(int durationDays, int min, int max) =>
        durationDays >= min && durationDays <= max;
}
