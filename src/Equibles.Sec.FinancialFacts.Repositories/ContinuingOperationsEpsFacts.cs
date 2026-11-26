using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

/// <summary>
/// An issuer's diluted EPS as <see cref="ContinuingOperationsEps"/> folds its whole history: one
/// loader for the fact tools and a host's own readers, so a period reads one figure everywhere.
/// </summary>
public static class ContinuingOperationsEpsFacts
{
    /// <summary>
    /// The folded consolidated facts of every issuer asked for, the pick priority that goes with
    /// them, and the continuing-operations concepts a share-unit reader must classify.
    /// </summary>
    public sealed record History(
        List<FinancialFact> Facts,
        Dictionary<Guid, int> Priority,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> ConceptsById
    );

    public static async Task<History> Load(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        IReadOnlyCollection<Guid> issuerIds,
        CancellationToken cancellationToken = default
    )
    {
        FinancialConceptAliases.TryResolve(ContinuingOperationsEps.Alias, out var family);
        var familyIndex = family
            .Select((reference, index) => (reference, index))
            .ToDictionary(
                item => (item.reference.Taxonomy, item.reference.Tag),
                item => item.index
            );
        var refs = family.Concat(ContinuingOperationsEps.EvidenceRefs).ToList();
        var pairs = refs.Select(r => (r.Taxonomy, r.Tag)).ToHashSet();

        // GetMatching returns the taxonomy x tag cross product; narrow to the exact pairs.
        var concepts = (
            await financialConceptRepository
                .GetMatching(
                    refs.Select(r => r.Taxonomy).Distinct().ToList(),
                    refs.Select(r => r.Tag).Distinct().ToList()
                )
                .Select(c => new
                {
                    c.Id,
                    c.Taxonomy,
                    c.Tag,
                })
                .ToListAsync(cancellationToken)
        ).Where(c => pairs.Contains((c.Taxonomy, c.Tag))).ToList();
        var roles = new ContinuingOperationsEps.Concepts(
            concepts.Select(c => (c.Id, c.Taxonomy, c.Tag))
        );
        var familyPriority = concepts
            .Where(c => familyIndex.ContainsKey((c.Taxonomy, c.Tag)))
            .ToDictionary(c => c.Id, c => familyIndex[(c.Taxonomy, c.Tag)]);
        var conceptIds = concepts.Select(c => c.Id).ToList();
        var conceptsById = concepts.ToDictionary(
            c => c.Id,
            c => new FinancialConceptAliases.ConceptRef(c.Taxonomy, c.Tag)
        );

        var facts =
            conceptIds.Count == 0
                ? []
                : await financialFactRepository
                    .GetConsolidatedByIssuerIds(issuerIds)
                    .Where(f => conceptIds.Contains(f.FinancialConceptId))
                    .ToListAsync(cancellationToken);

        var priority = new Dictionary<Guid, int>(familyPriority);
        var folded = new List<FinancialFact>();
        foreach (var issuer in facts.GroupBy(f => f.EquityIssuerId))
        {
            var (issuerFacts, issuerPriority) = ContinuingOperationsEps.Fold(
                issuer.Where(f => familyPriority.ContainsKey(f.FinancialConceptId)).ToList(),
                issuer.Where(f => roles.IsEvidence(f.FinancialConceptId)).ToList(),
                roles,
                familyPriority
            );
            folded.AddRange(issuerFacts);
            foreach (var (conceptId, rank) in issuerPriority)
                priority.TryAdd(conceptId, rank);
        }
        return new History(folded, priority, conceptsById);
    }
}
