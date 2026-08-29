using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Data.FiscalPeriods;

/// <summary>
/// Resolves an as-reported period against a company's authoritative fiscal-year-end anchor.
/// </summary>
public static class ReportedFiscalPeriodResolver
{
    private const int FyeMatchWindowDays = 14;
    private const int EarlyJanuaryFiscalYearEndDay = 7;
    private const int AnnualMinDays = 350;
    private const int AnnualMaxDays = 380;
    private const int QuarterMinDays = 80;
    private const int QuarterMaxDays = 100;
    private const int HalfYearMinDays = 170;
    private const int HalfYearMaxDays = 190;
    private const int NineMonthMinDays = 260;

    // A 41-week cumulative third quarter spans 286 days. Keep the same bounded
    // tolerance used by the reported-statement lane for non-calendar filers.
    private const int NineMonthMaxDays = 292;

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
        var closest = ClosestTo(candidates, periodEnd);

        if (isAnnual || isInstant)
        {
            if (Math.Abs(closest.DayNumber - periodEnd.DayNumber) <= FyeMatchWindowDays)
                return (closest.Year, SecFiscalPeriod.FullYear);
            if (!isInstant || !classifyInterimInstants)
                return null;
        }

        var isQuarter = IsWithinDays(durationDays, QuarterMinDays, QuarterMaxDays);
        var isHalfYear = IsWithinDays(durationDays, HalfYearMinDays, HalfYearMaxDays);
        var isNineMonths = IsWithinDays(durationDays, NineMonthMinDays, NineMonthMaxDays);
        var interimInstant = isInstant && classifyInterimInstants;
        if (!interimInstant && !isQuarter && !isHalfYear && !isNineMonths)
            return null;

        // A 52/53-week close may spill a few days past the nominal FYE. Keep it in Q4 of
        // that fiscal year instead of treating it as Q1 of the following year.
        DateOnly endingFye;
        if (
            periodEnd.DayNumber > closest.DayNumber
            && periodEnd.DayNumber - closest.DayNumber <= FyeMatchWindowDays
        )
        {
            endingFye = closest;
        }
        else
        {
            var matches = candidates
                .Where(candidate => candidate.DayNumber >= periodEnd.DayNumber)
                .ToList();
            if (matches.Count == 0)
                return null;
            endingFye = matches.MinBy(candidate => candidate.DayNumber);
        }

        if (endingFye.DayNumber - periodEnd.DayNumber > AnnualMaxDays || endingFye.Year < 2)
            return null;

        var fiscalYearStart = endingFye.AddYears(-1).AddDays(1);
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

    private static DateOnly CreateSafe(int year, int month, int day)
    {
        if (year < 1)
            return DateOnly.MinValue;
        if (year > 9999)
            return DateOnly.MaxValue;
        return new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    private static DateOnly ClosestTo(DateOnly[] candidates, DateOnly target) =>
        candidates.MinBy(candidate => Math.Abs(candidate.DayNumber - target.DayNumber));

    private static bool IsWithinDays(int durationDays, int min, int max) =>
        durationDays >= min && durationDays <= max;
}
