using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

/// <summary>
/// Places a company's fiscal year in the calendar year holding most of its days, so peers with different
/// fiscal calendars line up (NVDA's year ended January 2026 is calendar 2025). Placement reads each fact's own
/// dates, never its fiscal-year stamp. A balance is placed where the year it closes would be: a stated date
/// belongs to the calendar year holding the middle of the twelve months before it, so a January balance sits
/// with the revenue of the year it ends.
/// </summary>
public static class CalendarYearAlignment
{
    // Half a 365-day year: the middle of the year a balance closes.
    private const int HalfYearDays = 182;

    // A year that holds less of its days than this in one calendar year is marked as splitting about evenly
    // (a June year end holds 184 of 365).
    public const double EvenSplitShare = 0.6;

    /// <summary>
    /// The calendar year a fact belongs to, or null for a duration that is not a whole fiscal year (a stub,
    /// year-to-date or multi-year span is never placed).
    /// </summary>
    public static int? CalendarYear(FinancialFact fact)
    {
        var span = fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
        if (span == 0)
            return fact.PeriodEnd.AddDays(-HalfYearDays).Year;
        if (span < FiscalPeriodSpanDays.MinAnnual || span > FiscalPeriodSpanDays.MaxAnnual)
            return null;
        return fact.PeriodStart.AddDays(span / 2).Year;
    }

    /// <summary>The share of a duration's days that fall in one calendar year; 1 for a balance.</summary>
    public static double ShareIn(FinancialFact fact, int year)
    {
        var span = fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
        if (span == 0)
            return 1;
        var first = Math.Max(fact.PeriodStart.DayNumber, new DateOnly(year, 1, 1).DayNumber);
        var last = Math.Min(fact.PeriodEnd.DayNumber, new DateOnly(year, 12, 31).DayNumber);
        return Math.Max(0, last - first) / (double)span;
    }

    /// <summary>
    /// Each company's fiscal year for calendar <paramref name="year"/>, from the picks of the two stamps that can
    /// hold it: the year ending in <paramref name="year"/> and the one ending in the next. A company with neither
    /// placed in that year has no row; the neighbouring year never stands in for it. Two picks of the same period
    /// keep the earlier stamp, the period's own filing; two different periods keep the one with more days in the year.
    /// </summary>
    public static Dictionary<Guid, (FinancialFact Fact, int Stamp)> Choose(
        int year,
        IReadOnlyDictionary<Guid, FinancialFact> endingInYear,
        IReadOnlyDictionary<Guid, FinancialFact> endingNextYear
    )
    {
        var chosen = new Dictionary<Guid, (FinancialFact Fact, int Stamp)>();
        foreach (
            var (stock, fact, stamp) in endingInYear
                .Select(entry => (entry.Key, entry.Value, year))
                .Concat(endingNextYear.Select(entry => (entry.Key, entry.Value, year + 1)))
        )
        {
            if (CalendarYear(fact) != year)
                continue;
            if (!chosen.TryGetValue(stock, out var held))
            {
                chosen[stock] = (fact, stamp);
                continue;
            }
            var samePeriod =
                held.Fact.PeriodStart == fact.PeriodStart && held.Fact.PeriodEnd == fact.PeriodEnd;
            if (samePeriod ? stamp < held.Stamp : ShareIn(fact, year) > ShareIn(held.Fact, year))
                chosen[stock] = (fact, stamp);
        }
        return chosen;
    }
}
