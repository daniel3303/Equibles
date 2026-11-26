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
/// beside contract revenue whose year less nine months is 4,711M). An interim figure of a tag whose
/// year is the whole top line is one when its filing states another tag above it by a third for the
/// same period and it sits below a quarter of its share of that year (Legend Biotech's 6-Ks: contract
/// revenue 55.4M of 722.5M for nine months, its 20-F's year 1,028.9M).
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
    /// (no nine months, no figure ending on another date) drops no year and no quarter.
    /// </summary>
    public static List<FinancialFact> Without(IReadOnlyCollection<FinancialFact> revenue)
    {
        // Each rule judges the figures the others left (dropping an interim slice can leave its tag's
        // year without nine months), so the rules run until a pass drops nothing; a second call then
        // drops nothing either. Rules only drop, so the loop ends.
        var kept = revenue;
        while (true)
        {
            var slices = Slices(kept);
            if (slices.Count == 0)
                return [.. kept];
            kept = [.. kept.Where(f => !slices.Contains(f))];
        }
    }

    private static HashSet<object> Slices(IReadOnlyCollection<FinancialFact> revenue)
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
        slices.UnionWith(InterimSlices(standing));
        return slices;
    }

    // A tag can be the whole top line in its annual report and a slice in its interim reports (Legend
    // Biotech's 20-F tags 2025 contract revenue at 1,028.9M, its 6-Ks the nine months at 55.4M beside
    // 722.5M of revenue), so its year less its nine months is no quarter. An interim figure is a slice
    // where its own filing states another tag above it by a third for that exact period, it sits below a
    // quarter of its pro-rata share of the tag's year holding it, and that year is at least the other
    // tag's year in the same filing (the other tag's longest figure from the year's start, annualised,
    // where it states no figure for the year at all): a tag that is a narrower measure all year keeps its
    // figures, and so does a top line a swing in hedging or derivative revenue moves within its year.
    private static IEnumerable<FinancialFact> InterimSlices(
        IReadOnlyCollection<FinancialFact> facts
    )
    {
        var byFiling = facts.ToLookup(f => (f.AccessionNumber, f.PeriodStart, f.PeriodEnd, f.Unit));
        var byConcept = facts.ToLookup(f => (f.FinancialConceptId, f.Unit));
        FinancialFact YearHolding(Guid concept, string unit, FinancialFact period) =>
            byConcept[(concept, unit)]
                .Where(f =>
                    IsYear(f)
                    && f.PeriodStart <= period.PeriodStart
                    && f.PeriodEnd >= period.PeriodEnd
                )
                .MaxBy(f => f.FiledDate);
        decimal? Yardstick(Guid other, string unit, FinancialFact year)
        {
            var otherFacts = byConcept[(other, unit)].ToList();
            var otherYears = otherFacts
                .Where(f => f.PeriodStart == year.PeriodStart && f.PeriodEnd == year.PeriodEnd)
                .ToList();
            if (otherYears.Count > 0)
            {
                // A year another filing states is no evidence on this filing's measure.
                var stated = otherYears
                    .Where(f => f.AccessionNumber == year.AccessionNumber)
                    .Select(f => f.Value)
                    .Distinct()
                    .ToList();
                return stated.Count == 1 ? stated[0] : null;
            }
            var longest = otherFacts
                .Where(f =>
                    f.PeriodStart == year.PeriodStart && f.PeriodEnd < year.PeriodEnd && Span(f) > 0
                )
                .OrderByDescending(Span)
                .ThenByDescending(f => f.FiledDate)
                .FirstOrDefault();
            return longest == null ? null : longest.Value * Span(year) / Span(longest);
        }
        return facts.Where(fact =>
        {
            if (
                fact.Value <= 0
                || Span(fact)
                    is < FiscalPeriodSpanDays.MinQuarter
                        or >= FiscalPeriodSpanDays.MinAnnual
            )
                return false;
            var year = YearHolding(fact.FinancialConceptId, fact.Unit, fact);
            return year != null
                && fact.Value
                    < year.Value * Span(fact) / Span(year) * ShareOfItsYearASliceStaysBelow
                && byFiling[(fact.AccessionNumber, fact.PeriodStart, fact.PeriodEnd, fact.Unit)]
                    .Where(other =>
                        other.FinancialConceptId != fact.FinancialConceptId
                        && fact.Value < other.Value * YearShareOfAnotherTagsYear
                    )
                    .Any(other =>
                        Yardstick(other.FinancialConceptId, fact.Unit, year) is { } yardstick
                        && year.Value >= yardstick * WholeYearShare
                    );
        });
    }

    // A year this close to the other tag's is the same top line, rounding and a late restatement aside.
    private const decimal WholeYearShare = 0.95m;

    // A part of the whole top line sits near its pro-rata share of the year; a quarter of that share is
    // no seasonality or swing within one measure.
    private const decimal ShareOfItsYearASliceStaysBelow = 0.25m;

    private static IEnumerable<FinancialFact> QuarterSlices(
        IReadOnlyCollection<FinancialFact> facts
    )
    {
        var cumulativeEnds = facts
            .Where(f => Span(f) > FiscalPeriodSpanDays.MaxQuarter)
            .ToLookup(f => (f.FinancialConceptId, f.Unit), f => f.PeriodEnd);
        // The tag's missing cumulative is evidence only where the caller passed the history around the
        // quarter: a one-period read (CompareFinancialFact's bucket) holds no longer figure ending on
        // another date, and drops no quarter.
        var historyEnds = facts
            .Where(f => Span(f) > FiscalPeriodSpanDays.MaxQuarter)
            .ToLookup(f => f.Unit, f => f.PeriodEnd);
        var byFilingEnd = facts.ToLookup(f => (f.AccessionNumber, f.PeriodEnd, f.Unit));
        var nineMonthsByStart = facts
            .Where(IsNineMonths)
            .ToLookup(f => (f.FinancialConceptId, f.PeriodStart, f.Unit));
        return facts.Where(fact =>
            fact.Value > 0
            && Span(fact)
                is >= FiscalPeriodSpanDays.MinQuarter
                    and <= FiscalPeriodSpanDays.MaxQuarter
            && historyEnds[fact.Unit]
                .Any(end =>
                    end != fact.PeriodEnd
                    && Math.Abs(end.DayNumber - fact.PeriodEnd.DayNumber) <= YearDays
                )
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
