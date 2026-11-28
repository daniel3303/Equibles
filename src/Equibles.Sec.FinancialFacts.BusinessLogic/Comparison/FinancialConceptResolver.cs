using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Comparison;

// Resolves a concept alias to its ingested concepts, and the companion lines some aliases fold in (a bank's top line,
// operating costs, what proves a gross profit mis-tagged). Shared by the fact readers so every surface folds alike.
public class FinancialConceptResolver
{
    private readonly StatementFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;

    public FinancialConceptResolver(
        StatementFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository
    )
    {
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
    }

    // Per issuer, whether its operating costs continue the alias (OperatingCostsContinuation),
    // from each issuer's whole consolidated history of the three cost lines; the operating-costs
    // variants join conceptsById so a folded row reads its own variant.
    public async Task<(
        HashSet<Guid> Ids,
        HashSet<Guid> ClaimedStockIds
    )> ResolveOperatingCostsClaims(
        string alias,
        IReadOnlyCollection<Guid> stockIds,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> conceptsById
    )
    {
        var (operatingCostIds, operatingCostsById) = await ResolveAliasConcepts(
            OperatingCostsContinuation.OperatingCostsAlias
        );
        if (operatingCostIds.Count == 0)
            return ([], []);
        var (operatingExpenseIds, _) = await ResolveAliasConcepts(
            OperatingCostsContinuation.OperatingExpensesAlias
        );
        var (totalCostIds, _) = await ResolveAliasConcepts(
            OperatingCostsContinuation.TotalCostsAlias
        );
        var ids = operatingCostIds.Concat(operatingExpenseIds).Concat(totalCostIds).ToList();
        var history = await _financialFactRepository
            .GetConsolidatedByIssuerIds(stockIds)
            .Where(f => ids.Contains(f.FinancialConceptId))
            .ToListAsync();
        var claimed = history
            .GroupBy(f => f.EquityIssuerId)
            .Where(g =>
                OperatingCostsContinuation.Claimant(
                    g.Where(f => operatingCostIds.Contains(f.FinancialConceptId)).ToList(),
                    g.Where(f => operatingExpenseIds.Contains(f.FinancialConceptId)).ToList(),
                    g.Where(f => totalCostIds.Contains(f.FinancialConceptId)).ToList()
                ) == alias
            )
            .Select(g => g.Key)
            .ToHashSet();
        foreach (var (conceptId, conceptRef) in operatingCostsById)
            conceptsById.TryAdd(conceptId, conceptRef);
        return (operatingCostIds, claimed);
    }

    // Operating income, revenue and cost of revenue, in that order: what proves a gross profit
    // holds operating income (GrossProfitMistag). Empty when any is not ingested.
    public async Task<
        List<(HashSet<Guid> Ids, Dictionary<Guid, int> Priority)>
    > ResolveGrossProfitComponents()
    {
        var components = new List<(HashSet<Guid> Ids, Dictionary<Guid, int> Priority)>();
        foreach (
            var alias in new[]
            {
                GrossProfitMistag.OperatingIncomeAlias,
                GrossProfitMistag.RevenueAlias,
                GrossProfitMistag.CostOfRevenueAlias,
            }
        )
        {
            FinancialConceptAliases.TryResolve(alias, out var refs);
            var (priority, _) = await ResolveConcepts(refs);
            if (priority.Count == 0)
                return [];
            components.Add(([.. priority.Keys], priority));
        }
        return components;
    }

    public async Task<(
        HashSet<Guid> Ids,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> ConceptsById
    )> ResolveAliasConcepts(string alias)
    {
        FinancialConceptAliases.TryResolve(alias, out var refs);
        var (priority, conceptsById) = await ResolveConcepts(refs);
        return ([.. priority.Keys], conceptsById);
    }

    public async Task<(
        HashSet<Guid> NetInterestIncome,
        HashSet<Guid> NoninterestIncome,
        HashSet<Guid> InterestExpense
    )> ResolveBankTopLineComponentIds()
    {
        FinancialConceptAliases.TryResolve(BankTopLine.NetInterestIncomeAlias, out var netInterest);
        FinancialConceptAliases.TryResolve(BankTopLine.NoninterestIncomeAlias, out var noninterest);
        FinancialConceptAliases.TryResolve(
            BankTopLine.InterestExpenseAlias,
            out var interestExpense
        );
        var (netInterestPriority, _) = await ResolveConcepts(netInterest);
        var (noninterestPriority, _) = await ResolveConcepts(noninterest);
        var (interestExpensePriority, _) = await ResolveConcepts(interestExpense);
        return (
            [.. netInterestPriority.Keys],
            [.. noninterestPriority.Keys],
            [.. interestExpensePriority.Keys]
        );
    }

    // The alias's ingested concepts: their pick priority, and the variant each was reported
    // under, which decides how a split moves its values.
    public async Task<(
        Dictionary<Guid, int> Priority,
        Dictionary<Guid, FinancialConceptAliases.ConceptRef> ConceptsById
    )> ResolveConcepts(IReadOnlyList<FinancialConceptAliases.ConceptRef> conceptRefs)
    {
        var taxonomies = conceptRefs.Select(c => c.Taxonomy).Distinct().ToList();
        var tags = conceptRefs.Select(c => c.Tag).Distinct().ToList();
        // (taxonomy, tag) → position in the alias's ordered tag list; lower
        // index is the alias's preferred/primary concept.
        var priorityByPair = new Dictionary<(FactTaxonomy, string), int>();
        for (var i = 0; i < conceptRefs.Count; i++)
            priorityByPair.TryAdd((conceptRefs[i].Taxonomy, conceptRefs[i].Tag), i);

        var rows = await _financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => new
            {
                c.Id,
                c.Taxonomy,
                c.Tag,
            })
            .ToListAsync();

        // GetMatching is a taxonomy×tag cross product over a tiny alias tag set
        // (over-fetch is negligible here); narrow to the alias's exact pairs.
        var known = rows.Where(r => priorityByPair.ContainsKey((r.Taxonomy, r.Tag))).ToList();
        return (
            known.ToDictionary(r => r.Id, r => priorityByPair[(r.Taxonomy, r.Tag)]),
            known.ToDictionary(r => r.Id, r => conceptRefs[priorityByPair[(r.Taxonomy, r.Tag)]])
        );
    }
}
