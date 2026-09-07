using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Shared selection helpers for rendering <see cref="StatementLine"/>s, so the
/// Web tab, the MCP tool and any other statement surface resolve a line's tag
/// variants identically: the first variant (declaration order) the company
/// reported wins, later variants only fill the gap.
/// </summary>
public static class StatementLineFacts
{
    /// <summary>
    /// Every distinct (taxonomy, tag) pair across the lines' variants — the
    /// inputs for a FinancialConceptRepository.GetMatching lookup.
    /// </summary>
    public static (List<FactTaxonomy> Taxonomies, List<string> Tags) CollectConceptPairs(
        IEnumerable<StatementLine> lines
    )
    {
        var refs = lines.SelectMany(l => l.Concepts).ToList();
        return (
            refs.Select(r => r.Taxonomy).Distinct().ToList(),
            refs.Select(r => r.Tag).Distinct().ToList()
        );
    }

    // The longest span a discrete fiscal quarter can cover — 13 weeks on a
    // 4-4-5 calendar plus the occasional 14-week quarter, with headroom.
    private const int MaxDiscreteQuarterDays = 100;

    // The shortest span a fiscal year can cover — 52 weeks on a 52/53-week
    // calendar, with headroom for short transition years.
    private const int MinAnnualSpanDays = 350;

    // Ordinary annual facts never span more than a 53-week fiscal year plus
    // calendar drift. Longer durations are inception-to-date or multi-year.
    public const int MaxSupportedDurationDays = 380;

    /// <summary>
    /// The statement's own reporting endpoint, and the facts that share it. A
    /// filing re-reports comparative prior endpoints under one fiscal stamp, so
    /// anchoring keeps a statement from mixing two reporting dates.
    /// </summary>
    /// <remarks>
    /// A flow statement ends where the spans that MEASURE ITS PERIOD end. A fact
    /// filed under the stamp but measuring something else — a payment window
    /// (OPRA's 2023-01-12 dividend, filed as FY2022), a point disclosure, a
    /// cumulative or dimensional span — can be dated later and drag the anchor
    /// off the period, dropping every real line. Falls back to any measured span,
    /// then to every fact, so a point-only statement (every balance sheet) still
    /// anchors on its latest point.
    /// </remarks>
    public static List<FinancialFact> AnchorToLatestPeriodEnd(
        IReadOnlyCollection<FinancialFact> facts,
        SecFiscalPeriod fiscalPeriod
    )
    {
        if (facts.Count == 0)
            return [];

        var conforming = facts.Where(f => MeasuresGranularity(f, fiscalPeriod)).ToList();
        var spans = facts.Where(f => f.PeriodEnd > f.PeriodStart).ToList();
        var anchoring =
            conforming.Count > 0 ? conforming
            : spans.Count > 0 ? spans
            : facts;
        var statementPeriodEnd = anchoring.Max(f => f.PeriodEnd);
        return facts.Where(f => f.PeriodEnd == statementPeriodEnd).ToList();
    }

    // A span that measures exactly the requested granularity — the gate
    // PickCurrentlyReported already applies to a line. Anchoring on anything else lands
    // the statement on a date no line can serve, and it renders empty.
    private static bool MeasuresGranularity(FinancialFact fact, SecFiscalPeriod fiscalPeriod)
    {
        var spanDays = fact.PeriodEnd.DayNumber - fact.PeriodStart.DayNumber;
        return fiscalPeriod == SecFiscalPeriod.FullYear
            ? spanDays >= MinAnnualSpanDays && spanDays <= MaxSupportedDurationDays
            : spanDays >= 1 && spanDays <= MaxDiscreteQuarterDays;
    }

    /// <summary>
    /// The currently-reported fact among a fiscal period's candidates. A 10-Q
    /// reports each flow line twice under that identity — the discrete quarter
    /// and the fiscal year-to-date — and a balance-sheet line carries the
    /// period-end instant alongside re-stated comparative instants. Prefer the
    /// span matching the period's granularity (instants span zero days and
    /// always qualify), then the candidate ending latest so a comparative
    /// column never stands in for the current one, then a canonical periodic
    /// source and the latest restatement among same-ending candidates (#1546).
    /// </summary>
    public static FinancialFact PickCurrentlyReported(
        IEnumerable<FinancialFact> facts,
        SecFiscalPeriod fiscalPeriod
    )
    {
        var candidates = facts.ToList();

        // A source stamp never turns a cumulative or inception duration into
        // one quarter or one fiscal year.
        candidates = candidates
            .Where(f =>
            {
                if (f.PeriodType != FactPeriodType.Duration)
                    return true;
                var spanDays = f.PeriodEnd.DayNumber - f.PeriodStart.DayNumber;
                return spanDays is >= 0 and <= MaxSupportedDurationDays;
            })
            .ToList();
        if (candidates.Count == 0)
            return null;

        var preferred = candidates
            .Where(f =>
            {
                var spanDays = f.PeriodEnd.DayNumber - f.PeriodStart.DayNumber;
                return fiscalPeriod == SecFiscalPeriod.FullYear
                    ? spanDays == 0
                        || spanDays >= MinAnnualSpanDays && spanDays <= MaxSupportedDurationDays
                    : spanDays <= MaxDiscreteQuarterDays;
            })
            .ToList();
        if (preferred.Count == 0)
            return null;
        candidates = preferred;

        // A period a measured span already covers is never read off a point
        // disclosure in the same bucket, whose date can fall after the period end
        // and would win the ordering below. Point-only buckets — every
        // balance-sheet concept — are untouched.
        var spans = candidates.Where(f => f.PeriodEnd > f.PeriodStart).ToList();
        if (spans.Count > 0)
            candidates = spans;

        return candidates
            .OrderByDescending(f => f.PeriodEnd)
            .ThenBy(f => FinancialFactSourcePriority.Rank(f.Form))
            .ThenByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.AccessionNumber)
            .FirstOrDefault();
    }

    /// <summary>
    /// One renderable fact per concept. Concepts whose candidates do not prove
    /// the requested period are omitted so a line can fall through to its next
    /// declared tag variant.
    /// </summary>
    public static Dictionary<Guid, FinancialFact> PickCurrentlyReportedByConcept(
        IEnumerable<FinancialFact> facts,
        SecFiscalPeriod fiscalPeriod
    )
    {
        return facts
            .GroupBy(f => f.FinancialConceptId)
            .Select(g => new { ConceptId = g.Key, Fact = PickCurrentlyReported(g, fiscalPeriod) })
            .Where(x => x.Fact != null)
            .ToDictionary(x => x.ConceptId, x => x.Fact!);
    }

    /// <summary>
    /// The fact to render for a line: the first reported variant, otherwise the
    /// first derived variant, or null when the company reported none of them.
    /// </summary>
    public static FinancialFact PickFact(
        StatementLine line,
        IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIdByKey,
        IReadOnlyDictionary<Guid, FinancialFact> factByConceptId
    )
    {
        FinancialFact derivedFallback = null;
        foreach (var reference in line.Concepts)
        {
            if (
                conceptIdByKey.TryGetValue((reference.Taxonomy, reference.Tag), out var conceptId)
                && factByConceptId.TryGetValue(conceptId, out var fact)
            )
            {
                if (!StatementQuarterDerivation.IsDerived(fact))
                    return fact;
                derivedFallback ??= fact;
            }
        }
        return derivedFallback;
    }
}
