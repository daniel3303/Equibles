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
