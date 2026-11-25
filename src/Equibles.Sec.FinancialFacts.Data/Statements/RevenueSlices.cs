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
/// losses), and a negative year is a net loss of a broader measure, never a slice.
/// </summary>
public static class RevenueSlices
{
    // A year below three quarters of another tag's year cannot be that year less anything one
    // quarter's revenue could hold.
    private const decimal YearShareOfAnotherTagsYear = 0.75m;

    /// <summary>
    /// <paramref name="revenue"/> without the slices. Each figure is judged against its own filing,
    /// so a restated year a later filing states alone stays, and a caller holding one period's facts
    /// (no nine months) drops no year.
    /// </summary>
    public static List<FinancialFact> Without(IReadOnlyCollection<FinancialFact> revenue)
    {
        var filed = revenue.Where(IsFiledConsolidatedDuration).ToList();
        var nineMonths = filed
            .Where(IsNineMonths)
            .ToLookup(f => (f.FinancialConceptId, f.PeriodStart, f.Unit), f => f.Value);
        var slices = filed
            .GroupBy(f => (f.AccessionNumber, f.PeriodStart, f.PeriodEnd, f.Unit))
            .SelectMany(filing => filing.Where(fact => IsSlice(fact, filing, nineMonths)))
            .ToHashSet(ReferenceEqualityComparer.Instance);
        return slices.Count == 0 ? [.. revenue] : [.. revenue.Where(f => !slices.Contains(f))];
    }

    private static bool IsSlice(
        FinancialFact fact,
        IEnumerable<FinancialFact> filing,
        ILookup<(Guid, DateOnly, string), decimal> nineMonths
    )
    {
        var otherTags = filing.Where(other => other.FinancialConceptId != fact.FinancialConceptId);
        if (fact.Value == 0)
            return otherTags.Any(other => other.Value > 0);
        if (fact.Value < 0 || !IsYear(fact))
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
