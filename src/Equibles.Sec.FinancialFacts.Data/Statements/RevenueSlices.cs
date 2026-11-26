using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Revenue figures their own filing shows to be a slice of the top line, which the revenue alias
/// would otherwise pick for its first-ranked tag. A zero is one wherever the same filing states a
/// positive figure for the same period under another revenue tag (Flowserve tags Revenues 0 beside
/// 4,557.8M of contract revenue). A year is one when it sits below three quarters of another tag's
/// year in the same filing, that other tag states the year's nine months, and the year's own tag
/// states no nine months it covers (BlackRock fiscal 2024: Revenues 12,794M against 20,407M of
/// contract revenue whose nine months were 14,730M). A tag whose quarters add up inside its year
/// keeps it (Philip Morris's revenue net of excise taxes, a utility's revenue net of hedging
/// losses), and a negative year is a net loss of a broader measure, never a slice. A quarter is
/// one when its tag states no longer period within a year of it and it sits below three quarters
/// of another tag's figure for that quarter in the same filing, stated or the year that filing
/// states less the nine months filed with it (Teva's 10-K tags fourth-quarter 2025 Revenues 1,500M
/// beside contract revenue whose year less nine months is 4,711M).
/// </summary>
public static class RevenueSlices
{
    // A year below three quarters of another tag's year cannot be that year less anything one
    // quarter's revenue could hold; the same share marks a quarter.
    private const decimal YearShareOfAnotherTagsYear = 0.75m;

    // A tag's longer figure this close to a quarter makes the tag a series of its own there.
    private const int YearDays = 400;

    /// <summary>
    /// <paramref name="revenue"/> without the slices. Each figure is judged against its own filing,
    /// so a restated year a later filing states alone stays, and a caller holding one period's facts
    /// (no nine months) drops no year.
    /// </summary>
    public static List<FinancialFact> Without(IReadOnlyCollection<FinancialFact> revenue)
    {
        var filings = revenue
            .Where(IsFiledConsolidatedDuration)
            .GroupBy(f => (f.AccessionNumber, f.PeriodStart, f.PeriodEnd, f.Unit))
            .ToList();
        var slices = filings
            .SelectMany(filing => filing.Where(fact => IsZeroSlice(fact, filing)))
            .ToHashSet(ReferenceEqualityComparer.Instance);
        // A zero slice is no nine months the year's tag states, so a second call, which no longer
        // sees it, judges every year as the first did.
        var nineMonths = filings
            .SelectMany(filing => filing)
            .Where(f => IsNineMonths(f) && !slices.Contains(f))
            .ToLookup(f => (f.FinancialConceptId, f.PeriodStart, f.Unit), f => f.Value);
        slices.UnionWith(
            filings.SelectMany(filing =>
                filing.Where(fact => IsYearSlice(fact, filing, nineMonths))
            )
        );
        // Quarters are judged against figures neither rule above dropped, which a second call
        // still sees, so it judges every quarter as the first did.
        var standing = filings
            .SelectMany(filing => filing)
            .Where(f => !slices.Contains(f))
            .ToList();
        slices.UnionWith(QuarterSlices(standing));
        return slices.Count == 0 ? [.. revenue] : [.. revenue.Where(f => !slices.Contains(f))];
    }

    private static IEnumerable<FinancialFact> QuarterSlices(
        IReadOnlyCollection<FinancialFact> facts
    )
    {
        var cumulativeEnds = facts
            .Where(f => Span(f) > FiscalPeriodSpanDays.MaxQuarter)
            .ToLookup(f => (f.FinancialConceptId, f.Unit), f => f.PeriodEnd);
        var byFilingEnd = facts.ToLookup(f => (f.AccessionNumber, f.PeriodEnd, f.Unit));
        var nineMonthsByStart = facts
            .Where(IsNineMonths)
            .ToLookup(f => (f.FinancialConceptId, f.PeriodStart, f.Unit));
        return facts.Where(fact =>
            fact.Value > 0
            && Span(fact)
                is >= FiscalPeriodSpanDays.MinQuarter
                    and <= FiscalPeriodSpanDays.MaxQuarter
            // A tag with a cumulative or a year around the quarter is a measure of its own.
            && !cumulativeEnds[(fact.FinancialConceptId, fact.Unit)]
                .Any(end => Math.Abs(end.DayNumber - fact.PeriodEnd.DayNumber) <= YearDays)
            && byFilingEnd[(fact.AccessionNumber, fact.PeriodEnd, fact.Unit)]
                .Where(other => other.FinancialConceptId != fact.FinancialConceptId)
                .Any(other =>
                    fact.Value
                    < SameQuarter(other, fact, nineMonthsByStart) * YearShareOfAnotherTagsYear
                )
        );
    }

    // The other tag's figure for the quarter: stated for it, or its year less the nine months it
    // was filed with (the latest filed on or before the year), so a later recast never meets it.
    private static decimal SameQuarter(
        FinancialFact other,
        FinancialFact quarter,
        ILookup<(Guid, DateOnly, string), FinancialFact> nineMonthsByStart
    )
    {
        if (other.PeriodStart == quarter.PeriodStart)
            return other.Value;
        if (!IsYear(other) || other.PeriodStart >= quarter.PeriodStart)
            return 0;
        var nineMonths = nineMonthsByStart[
            (other.FinancialConceptId, other.PeriodStart, other.Unit)
        ]
            .Where(f =>
                f.PeriodEnd.AddDays(1) == quarter.PeriodStart && f.FiledDate <= other.FiledDate
            )
            .MaxBy(f => f.FiledDate);
        return nineMonths == null ? 0 : other.Value - nineMonths.Value;
    }

    private static bool IsZeroSlice(FinancialFact fact, IEnumerable<FinancialFact> filing) =>
        fact.Value == 0
        && filing.Any(other =>
            other.FinancialConceptId != fact.FinancialConceptId && other.Value > 0
        );

    private static bool IsYearSlice(
        FinancialFact fact,
        IEnumerable<FinancialFact> filing,
        ILookup<(Guid, DateOnly, string), decimal> nineMonths
    )
    {
        var otherTags = filing.Where(other => other.FinancialConceptId != fact.FinancialConceptId);
        if (fact.Value <= 0 || !IsYear(fact))
            return false;
        if (
            nineMonths[(fact.FinancialConceptId, fact.PeriodStart, fact.Unit)]
                .Any(v => v <= fact.Value)
        )
            return false;
        return otherTags.Any(other =>
            fact.Value < other.Value * YearShareOfAnotherTagsYear
            && nineMonths[(other.FinancialConceptId, other.PeriodStart, other.Unit)].Any()
        );
    }

    // A derived quarter carries no filing, so it is no filing's evidence either way.
    private static bool IsFiledConsolidatedDuration(FinancialFact fact) =>
        fact.PeriodType == FactPeriodType.Duration
        && fact.DimensionsKey == ""
        && !string.IsNullOrEmpty(fact.AccessionNumber)
        && Span(fact) is > 0 and <= FiscalPeriodSpanDays.MaxAnnual;

    private static bool IsYear(FinancialFact fact) => Span(fact) >= FiscalPeriodSpanDays.MinAnnual;

    // Three quarters from the year's start: longer than two quarters can be, shorter than a year.
    private static bool IsNineMonths(FinancialFact fact) =>
        Span(fact) is > 2 * FiscalPeriodSpanDays.MaxQuarter and < FiscalPeriodSpanDays.MinAnnual;

    private static int Span(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
}
