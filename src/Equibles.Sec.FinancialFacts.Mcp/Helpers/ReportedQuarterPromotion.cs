using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

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
internal static class ReportedQuarterPromotion
{
    /// <summary>
    /// A full history load: returns the input plus one Q4-stamped copy of every
    /// reported discrete fourth quarter filed under fp=FY, its fiscal year taken
    /// from the smallest stamp among the annual-span facts ending the same day.
    /// Originals are kept (the annual group's span gate already ignores them);
    /// the input is never mutated.
    /// </summary>
    internal static List<FinancialFact> WithPromotedFourthQuarters(List<FinancialFact> facts)
    {
        var fiscalYearByYearEnd = facts
            .Where(f =>
                f.FiscalPeriod == SecFiscalPeriod.FullYear
                && f.PeriodType == FactPeriodType.Duration
                && Span(f) >= FiscalPeriodSpanDays.MinAnnualSpanDays
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
    internal static IEnumerable<FinancialFact> PromotedFourthQuartersForYearSlice(
        IEnumerable<FinancialFact> companyFacts
    )
    {
        var fullYearDurations = companyFacts
            .Where(f =>
                f.FiscalPeriod == SecFiscalPeriod.FullYear
                && f.PeriodType == FactPeriodType.Duration
            )
            .ToList();
        var annualEnds = fullYearDurations
            .Where(f => Span(f) >= FiscalPeriodSpanDays.MinAnnualSpanDays)
            .Select(f => f.PeriodEnd)
            .ToList();
        if (annualEnds.Count == 0)
            return [];

        var ownYearEnd = annualEnds.Max();
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
        return span >= FiscalPeriodSpanDays.MinDiscreteQuarterDays
            && span <= FiscalPeriodSpanDays.MaxDiscreteQuarterDays;
    }

    // A copy, never a mutation — the source rows may be EF-tracked entities.
    private static FinancialFact Promote(FinancialFact fact, int fiscalYear) =>
        new()
        {
            CommonStockId = fact.CommonStockId,
            FinancialConceptId = fact.FinancialConceptId,
            DocumentId = fact.DocumentId,
            Unit = fact.Unit,
            PeriodType = fact.PeriodType,
            PeriodStart = fact.PeriodStart,
            PeriodEnd = fact.PeriodEnd,
            Value = fact.Value,
            FiscalYear = fiscalYear,
            FiscalPeriod = SecFiscalPeriod.Q4,
            Form = fact.Form,
            FiledDate = fact.FiledDate,
            AccessionNumber = fact.AccessionNumber,
            Frame = fact.Frame,
            DimensionsKey = fact.DimensionsKey,
        };

    private static int Span(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
}
