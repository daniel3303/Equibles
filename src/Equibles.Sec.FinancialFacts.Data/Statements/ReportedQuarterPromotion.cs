using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// SEC Company Facts files a filer's REPORTED discrete fourth quarter under
/// fp=FY — the same fiscal identity as the annual figure. Tools that group facts
/// by (FiscalYear, FiscalPeriod) span-gate that row out of the annual group
/// (correctly: the year must show the annual figure) but never see it as a
/// quarter, so Q4 rows were absent for every filer whose Company Facts carry Q4
/// frames (NVDA through FY2020). Promotion re-homes those rows onto the Q4
/// identity so the company's own reported figure surfaces.
///
/// A full-year total can never be promoted: only durations spanning a discrete
/// quarter qualify, and only when a genuine annual-span fact ends the same day —
/// that anchor also supplies the fiscal year, because comparative re-filings
/// re-stamp the same period under the filing's later year (the original filing
/// carries the smallest stamp). A fiscal-year-change transition stub (a ~90-day
/// "year" with no annual-span fact ending that day) has no anchor and keeps its
/// annual identity.
/// </summary>
public static class ReportedQuarterPromotion
{
    /// <summary>
    /// A full history load: returns the input plus one Q4-stamped copy of every
    /// reported discrete fourth quarter filed under fp=FY, its fiscal year taken
    /// from the smallest stamp among the annual-span facts ending the same day.
    /// Originals are kept (the annual group's span gate already ignores them);
    /// the input is never mutated.
    /// </summary>
    public static IReadOnlyCollection<FinancialFact> WithPromotedFourthQuarters(
        IReadOnlyCollection<FinancialFact> facts
    )
    {
        var fiscalYearByYearEnd = facts
            .Where(f =>
                f.FiscalPeriod == SecFiscalPeriod.FullYear
                && f.PeriodType == FactPeriodType.Duration
                && Span(f) >= FiscalPeriodSpanDays.MinAnnual
                && Span(f) <= FiscalPeriodSpanDays.MaxAnnual
            )
            .GroupBy(f => f.PeriodEnd)
            .ToDictionary(g => g.Key, g => g.Min(f => f.FiscalYear));
        if (fiscalYearByYearEnd.Count == 0)
            return facts;

        var promoted = facts
            .Where(IsQuarterSpanFullYearDuration)
            .Where(f => fiscalYearByYearEnd.ContainsKey(f.PeriodEnd))
            .Select(f => Promote(f, fiscalYearByYearEnd[f.PeriodEnd]))
            .ToList();
        if (promoted.Count == 0)
            return facts;

        return [.. facts, .. promoted];
    }

    /// <summary>
    /// A year-scoped load (one fiscal-year stamp per company): promotes only the
    /// quarter-span fp=FY rows ending exactly where the slice's own year does —
    /// the LATEST annual-span period end per company. Comparative re-filings in
    /// the slice end earlier and must not pose as the requested year's fourth
    /// quarter (their value belongs to the prior year).
    /// </summary>
    public static IEnumerable<FinancialFact> PromotedFourthQuartersForYearSlice(
        IEnumerable<FinancialFact> companyFacts
    )
    {
        var fullYearDurations = companyFacts
            .Where(f =>
                f.FiscalPeriod == SecFiscalPeriod.FullYear
                && f.PeriodType == FactPeriodType.Duration
            )
            .ToList();
        if (fullYearDurations.Count == 0)
            return [];

        var ownYearEnd = fullYearDurations.Max(f => f.PeriodEnd);
        var hasOwnYearAnchor = fullYearDurations.Any(f =>
            f.PeriodEnd == ownYearEnd
            && Span(f) >= FiscalPeriodSpanDays.MinAnnual
            && Span(f) <= FiscalPeriodSpanDays.MaxAnnual
        );
        if (!hasOwnYearAnchor)
            return [];

        return fullYearDurations
            .Where(IsQuarterSpanFullYearDuration)
            .Where(f => f.PeriodEnd == ownYearEnd)
            .Select(f => Promote(f, f.FiscalYear));
    }

    private static bool IsQuarterSpanFullYearDuration(FinancialFact fact)
    {
        if (
            fact.FiscalPeriod != SecFiscalPeriod.FullYear
            || fact.PeriodType != FactPeriodType.Duration
        )
            return false;
        var span = Span(fact);
        return span >= FiscalPeriodSpanDays.MinQuarter && span <= FiscalPeriodSpanDays.MaxQuarter;
    }

    private static FinancialFact Promote(FinancialFact fact, int fiscalYear) =>
        fact.Restamped(fiscalYear, SecFiscalPeriod.Q4);

    private static int Span(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
}
