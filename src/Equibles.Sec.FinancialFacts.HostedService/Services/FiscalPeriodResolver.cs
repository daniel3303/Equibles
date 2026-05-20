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
    /// </summary>
    public static (int Year, SecFiscalPeriod Period)? Resolve(
        DateOnly periodStart,
        DateOnly periodEnd,
        int? fyeMonth,
        int? fyeDay
    )
    {
        if (fyeMonth is null || fyeDay is null)
            return null;
        if (fyeMonth < 1 || fyeMonth > 12 || fyeDay < 1 || fyeDay > 31)
            return null;

        var candidates = new[]
        {
            CreateSafe(periodEnd.Year - 1, fyeMonth.Value, fyeDay.Value),
            CreateSafe(periodEnd.Year, fyeMonth.Value, fyeDay.Value),
            CreateSafe(periodEnd.Year + 1, fyeMonth.Value, fyeDay.Value),
        };

        var durationDays = periodEnd.DayNumber - periodStart.DayNumber;
        var isInstant = durationDays == 0;
        var isAnnual = durationDays >= AnnualMinDays && durationDays <= AnnualMaxDays;

        if (isAnnual || isInstant)
        {
            var closest = ClosestTo(candidates, periodEnd);
            if (Math.Abs(closest.DayNumber - periodEnd.DayNumber) > FyeMatchWindowDays)
                return null;
            return (closest.Year, SecFiscalPeriod.FullYear);
        }

        var isQuarter = durationDays >= QuarterMinDays && durationDays <= QuarterMaxDays;
        var isHalfYear = durationDays >= HalfYearMinDays && durationDays <= HalfYearMaxDays;
        var isNineMonths = durationDays >= NineMonthMinDays && durationDays <= NineMonthMaxDays;

        if (!isQuarter && !isHalfYear && !isNineMonths)
            return null;

        // The fiscal year containing periodEnd is the one whose FYE is on or
        // after periodEnd within roughly twelve months — ranks the matched
        // year correctly even when the quarter closes a few days late.
        DateOnly endingFye = default;
        var endingFyeFound = false;
        foreach (var candidate in candidates)
        {
            if (candidate.DayNumber < periodEnd.DayNumber)
                continue;
            if (!endingFyeFound || candidate.DayNumber < endingFye.DayNumber)
            {
                endingFye = candidate;
                endingFyeFound = true;
            }
        }
        if (!endingFyeFound)
            return null;
        if (endingFye.DayNumber - periodEnd.DayNumber > AnnualMaxDays)
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
        var maxDay = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, Math.Min(day, maxDay));
    }

    private static DateOnly ClosestTo(DateOnly[] candidates, DateOnly target)
    {
        var best = candidates[0];
        var bestDistance = Math.Abs(best.DayNumber - target.DayNumber);
        for (var i = 1; i < candidates.Length; i++)
        {
            var distance = Math.Abs(candidates[i].DayNumber - target.DayNumber);
            if (distance < bestDistance)
            {
                best = candidates[i];
                bestDistance = distance;
            }
        }
        return best;
    }
}
