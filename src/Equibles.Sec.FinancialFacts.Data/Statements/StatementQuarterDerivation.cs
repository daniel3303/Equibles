using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Derives missing discrete USD quarters from consecutive cumulative facts.
/// The source facts remain unchanged; derived rows exist only in the read path.
/// </summary>
public static class StatementQuarterDerivation
{
    private const int MinQuarterDays = 80;
    private const int MaxQuarterDays = 100;
    private static readonly HashSet<string> NonNegativeAliases =
    [
        "depreciation-and-amortization",
        "share-based-compensation",
        "capital-expenditures",
        "acquisitions",
        "share-repurchases",
        "dividends-paid",
        "debt-issued",
        "debt-repaid",
        "stock-issued",
    ];

    public static IReadOnlyList<FinancialFact> AppendDerived(
        IReadOnlyCollection<FinancialFact> facts,
        IReadOnlySet<Guid> rejectNegativeConceptIds = null
    )
    {
        var result = facts.ToList();
        var spans = SelectSpans(facts);

        foreach (
            var concept in spans.GroupBy(s =>
                (s.Fact.CommonStockId, s.Fact.FinancialConceptId, s.Fact.Unit, s.Fact.DimensionsKey)
            )
        )
        {
            var conceptSpans = concept.ToList();
            foreach (var current in conceptSpans)
            {
                var prior = FindPrior(current, conceptSpans);
                if (prior == null || HasReportedQuarter(current, conceptSpans))
                    continue;

                var derived = Derive(prior, current);
                if (
                    derived.Value < 0m
                    && rejectNegativeConceptIds?.Contains(derived.FinancialConceptId) == true
                )
                    continue;

                result.Add(derived);
            }
        }

        return result;
    }

    public static bool IsDerived(FinancialFact fact) =>
        fact.Form == null && string.IsNullOrEmpty(fact.AccessionNumber);

    public static HashSet<Guid> GetNonNegativeConceptIds(
        IEnumerable<StatementLine> lines,
        IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIdByKey
    ) =>
        lines
            .Where(line => NonNegativeAliases.Contains(line.Alias))
            .SelectMany(line => line.Concepts)
            .Select(reference =>
                conceptIdByKey.GetValueOrDefault((reference.Taxonomy, reference.Tag))
            )
            .Where(id => id != Guid.Empty)
            .ToHashSet();

    private static List<SpanFact> SelectSpans(IReadOnlyCollection<FinancialFact> facts) =>
        facts
            .Where(f =>
                f.PeriodType == FactPeriodType.Duration
                && f.Unit == "USD"
                && f.PeriodEnd >= f.PeriodStart
                && Span(f) <= StatementLineFacts.MaxSupportedDurationDays
            )
            .GroupBy(f =>
                (
                    f.CommonStockId,
                    f.PeriodStart,
                    f.PeriodEnd,
                    f.FinancialConceptId,
                    f.Unit,
                    f.DimensionsKey
                )
            )
            .Select(g => new SpanFact(
                g.OrderBy(f => FinancialFactSourcePriority.Rank(f.Form))
                    .ThenByDescending(f => f.FiledDate)
                    .ThenByDescending(f => f.AccessionNumber)
                    .First(),
                g.OrderBy(f => f.FiscalYear)
                    .ThenBy(f => FinancialFactSourcePriority.Rank(f.Form))
                    .ThenByDescending(f => f.FiledDate)
                    .First()
            ))
            .ToList();

    private static SpanFact FindPrior(SpanFact current, IReadOnlyCollection<SpanFact> spans)
    {
        var priorPeriod = current.Identity.FiscalPeriod switch
        {
            SecFiscalPeriod.Q2 => SecFiscalPeriod.Q1,
            SecFiscalPeriod.Q3 => SecFiscalPeriod.Q2,
            _ => (SecFiscalPeriod?)null,
        };
        if (priorPeriod == null || !HasExpectedCurrentSpan(current))
            return null;

        return spans
            .Where(s =>
                s.Identity.FiscalYear == current.Identity.FiscalYear
                && s.Identity.FiscalPeriod == priorPeriod
                && s.Fact.PeriodStart == current.Fact.PeriodStart
                && HasExpectedPriorSpan(s, priorPeriod.Value)
                && IsQuarterRemainder(s.Fact.PeriodEnd, current.Fact.PeriodEnd)
            )
            .OrderByDescending(s => s.Fact.FiledDate)
            .ThenByDescending(s => s.Fact.AccessionNumber)
            .FirstOrDefault();
    }

    private static bool HasExpectedCurrentSpan(SpanFact current) =>
        current.Identity.FiscalPeriod switch
        {
            SecFiscalPeriod.Q2 => IsCumulativeSpan(current.Fact, 2),
            SecFiscalPeriod.Q3 => IsCumulativeSpan(current.Fact, 3),
            _ => false,
        };

    private static bool HasExpectedPriorSpan(SpanFact prior, SecFiscalPeriod period) =>
        period switch
        {
            SecFiscalPeriod.Q1 => IsQuarterSpan(prior.Fact),
            SecFiscalPeriod.Q2 => IsCumulativeSpan(prior.Fact, 2),
            SecFiscalPeriod.Q3 => IsCumulativeSpan(prior.Fact, 3),
            _ => false,
        };

    private static bool HasReportedQuarter(SpanFact current, IReadOnlyCollection<SpanFact> spans) =>
        spans.Any(s =>
            s.Fact.PeriodEnd == current.Fact.PeriodEnd
            && s.Identity.FiscalPeriod == current.Identity.FiscalPeriod
            && IsQuarterSpan(s.Fact)
        );

    private static FinancialFact Derive(SpanFact prior, SpanFact current) =>
        new()
        {
            CommonStockId = current.Fact.CommonStockId,
            FinancialConceptId = current.Fact.FinancialConceptId,
            Unit = current.Fact.Unit,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = prior.Fact.PeriodEnd.AddDays(1),
            PeriodEnd = current.Fact.PeriodEnd,
            Value = current.Fact.Value - prior.Fact.Value,
            FiscalYear = current.Identity.FiscalYear,
            FiscalPeriod = current.Identity.FiscalPeriod,
            FiledDate =
                current.Fact.FiledDate > prior.Fact.FiledDate
                    ? current.Fact.FiledDate
                    : prior.Fact.FiledDate,
            DimensionsKey = current.Fact.DimensionsKey,
        };

    private static bool IsQuarterSpan(FinancialFact fact) =>
        Span(fact) is >= MinQuarterDays and <= MaxQuarterDays;

    private static bool IsCumulativeSpan(FinancialFact fact, int quarters) =>
        Span(fact) >= MinQuarterDays * quarters && Span(fact) <= MaxQuarterDays * quarters;

    private static bool IsQuarterRemainder(DateOnly priorEnd, DateOnly currentEnd) =>
        currentEnd.DayNumber - priorEnd.DayNumber is >= MinQuarterDays and <= MaxQuarterDays;

    private static int Span(FinancialFact fact) =>
        fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;

    private sealed record SpanFact(FinancialFact Fact, FinancialFact Identity);
}
