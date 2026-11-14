using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

/// <summary>
/// An issuer's depreciation and amortization as <see cref="DepreciationTotal"/> folds its whole
/// history. One loader for the fact tools, the statement tool, the web tab and a host's own
/// readers, so a period reads one figure on every surface.
/// </summary>
public static class DepreciationTotalFacts
{
    /// <summary>
    /// The folded consolidated facts of every issuer asked for, the pick priority that goes with
    /// them, and the concepts the fold told apart.
    /// </summary>
    public sealed record History(
        List<FinancialFact> Facts,
        Dictionary<Guid, int> Priority,
        DepreciationTotal.Concepts Concepts
    );

    public static async Task<History> Load(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        IReadOnlyCollection<Guid> issuerIds,
        CancellationToken cancellationToken = default
    )
    {
        FinancialConceptAliases.TryResolve(DepreciationTotal.Alias, out var family);
        var familyIndex = family
            .Select((reference, index) => (reference, index))
            .ToDictionary(
                item => (item.reference.Taxonomy, item.reference.Tag),
                item => item.index
            );
        var refs = family.Concat(DepreciationTotal.PartRefs).ToList();
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
        var roles = new DepreciationTotal.Concepts(concepts.Select(c => (c.Id, c.Taxonomy, c.Tag)));
        var familyPriority = concepts
            .Where(c => familyIndex.ContainsKey((c.Taxonomy, c.Tag)))
            .ToDictionary(c => c.Id, c => familyIndex[(c.Taxonomy, c.Tag)]);
        var conceptIds = concepts.Select(c => c.Id).ToList();

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
            var (issuerFacts, issuerPriority) = DepreciationTotal.Fold(
                issuer.Where(f => familyPriority.ContainsKey(f.FinancialConceptId)).ToList(),
                issuer.Where(f => roles.IsPart(f.FinancialConceptId)).ToList(),
                roles,
                familyPriority
            );
            folded.AddRange(issuerFacts);
            foreach (var (conceptId, rank) in issuerPriority)
                priority.TryAdd(conceptId, rank);
        }
        return new History(folded, priority, roles);
    }

    /// <summary>
    /// A statement's consolidated facts with the issuer's slices left out and the sums of
    /// <paramref name="fiscalYear"/> added (<see cref="DepreciationTotal.ForStatement"/>).
    /// </summary>
    public static async Task<List<FinancialFact>> ForStatement(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        Guid issuerId,
        IReadOnlyCollection<FinancialFact> statementFacts,
        int fiscalYear,
        CancellationToken cancellationToken = default
    )
    {
        var history = await Load(
            financialFactRepository,
            financialConceptRepository,
            [issuerId],
            cancellationToken
        );
        return DepreciationTotal.ForStatement(
            statementFacts,
            fiscalYear,
            history.Facts,
            history.Concepts
        );
    }
}
