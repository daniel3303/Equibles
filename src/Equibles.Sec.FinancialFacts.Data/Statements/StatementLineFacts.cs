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

    /// <summary>
    /// The currently-reported fact among a fiscal period's candidates. A 10-Q
    /// reports each flow line twice under that identity — the discrete quarter
    /// and the fiscal year-to-date — and a balance-sheet line carries the
    /// period-end instant alongside re-stated comparative instants. Prefer the
    /// span matching the period's granularity (instants span zero days and
    /// always qualify), then the candidate ending latest so a comparative
    /// column never stands in for the current one, then the latest restatement
    /// among same-ending candidates (#1546).
    /// </summary>
    public static FinancialFact PickCurrentlyReported(
        IEnumerable<FinancialFact> facts,
        SecFiscalPeriod fiscalPeriod
    )
    {
        var candidates = facts.ToList();

        var preferred = candidates
            .Where(f =>
            {
                var spanDays = f.PeriodEnd.DayNumber - f.PeriodStart.DayNumber;
                return fiscalPeriod == SecFiscalPeriod.FullYear
                    ? spanDays == 0 || spanDays >= MinAnnualSpanDays
                    : spanDays <= MaxDiscreteQuarterDays;
            })
            .ToList();
        if (preferred.Count > 0)
            candidates = preferred;

        return candidates
            .OrderByDescending(f => f.PeriodEnd)
            .ThenByDescending(f => f.FiledDate)
            .First();
    }

    /// <summary>
    /// The fact to render for a line: its first variant with a fact for the
    /// period, or null when the company reported none of them.
    /// </summary>
    public static FinancialFact PickFact(
        StatementLine line,
        IReadOnlyDictionary<(FactTaxonomy Taxonomy, string Tag), Guid> conceptIdByKey,
        IReadOnlyDictionary<Guid, FinancialFact> factByConceptId
    )
    {
        foreach (var reference in line.Concepts)
        {
            if (
                conceptIdByKey.TryGetValue((reference.Taxonomy, reference.Tag), out var conceptId)
                && factByConceptId.TryGetValue(conceptId, out var fact)
            )
                return fact;
        }
        return null;
    }
}
