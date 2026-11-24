using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

/// <summary>
/// The net income line read with the owners' profit sign each issuer's filings prove
/// (<see cref="OwnersProfitSign"/>). One loader for the fact tools, the statement tool and a
/// host's own readers, so a period reads one sign on every surface.
/// </summary>
public static class OwnersProfitSignFacts
{
    /// <summary>
    /// <paramref name="facts"/> with the facts of <paramref name="lineConceptIds"/> (the net
    /// income line) read by <see cref="OwnersProfitSign.Read"/> against each issuer's whole
    /// history; every other fact passes through untouched. Whether an issuer is read is decided
    /// on its whole history, never on the slice asked for, so a one-year statement and the fact
    /// series agree. Evidence is loaded only for an issuer with a foreign issuer's report.
    /// </summary>
    public static async Task<OwnersProfitSign.Reading> Read(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        IReadOnlyCollection<Guid> issuerIds,
        IReadOnlyCollection<FinancialFact> facts,
        IReadOnlySet<Guid> lineConceptIds,
        CancellationToken cancellationToken = default
    )
    {
        var unchanged = OwnersProfitSign.Unchanged(facts);
        if (issuerIds.Count == 0 || !facts.Any(f => lineConceptIds.Contains(f.FinancialConceptId)))
            return unchanged;

        var pairs = OwnersProfitSign.EvidencePairs.Concat(OwnersProfitSign.OwnersPairs).ToHashSet();
        // GetMatching returns the taxonomy x tag cross product; narrow to the exact pairs.
        var concepts = (
            await financialConceptRepository
                .GetMatching(
                    pairs.Select(p => p.Taxonomy).Distinct().ToList(),
                    pairs.Select(p => p.Tag).Distinct().ToList()
                )
                .Select(c => new
                {
                    c.Id,
                    c.Taxonomy,
                    c.Tag,
                })
                .ToListAsync(cancellationToken)
        ).Where(c => pairs.Contains((c.Taxonomy, c.Tag))).ToList();
        var ownersIds = concepts
            .Where(c => OwnersProfitSign.IsOwnersProfit(c.Taxonomy, c.Tag))
            .Select(c => c.Id)
            .ToList();
        var evidenceConcepts = concepts
            .Where(c => !OwnersProfitSign.IsOwnersProfit(c.Taxonomy, c.Tag))
            .ToDictionary(c => c.Id, c => (c.Taxonomy, c.Tag));
        if (ownersIds.Count == 0 || evidenceConcepts.Count == 0)
            return unchanged;

        var ownersForms = await financialFactRepository
            .GetConsolidatedByIssuerIds(issuerIds)
            .Where(f => ownersIds.Contains(f.FinancialConceptId))
            .Select(f => new { f.EquityIssuerId, f.Form })
            .Distinct()
            .ToListAsync(cancellationToken);
        var readIssuerIds = ownersForms
            .GroupBy(f => f.EquityIssuerId)
            .Where(issuer => issuer.Any(f => OwnersProfitSign.IsForeignIssuerForm(f.Form)))
            .Select(issuer => issuer.Key)
            .ToList();
        if (readIssuerIds.Count == 0)
            return unchanged;

        var loadedIds = evidenceConcepts.Keys.Concat(ownersIds).ToList();
        var history = (
            await financialFactRepository
                .GetConsolidatedByIssuerIds(readIssuerIds)
                .Where(f => loadedIds.Contains(f.FinancialConceptId))
                .ToListAsync(cancellationToken)
        ).ToLookup(f => f.EquityIssuerId);
        var ownersIdSet = ownersIds.ToHashSet();
        var read = new List<FinancialFact>();
        var negated = new HashSet<FinancialFact>(ReferenceEqualityComparer.Instance);
        var withheld = new HashSet<(DateOnly, DateOnly, string)>();
        foreach (var issuer in facts.GroupBy(f => f.EquityIssuerId))
        {
            var line = issuer.Where(f => lineConceptIds.Contains(f.FinancialConceptId)).ToList();
            read.AddRange(issuer.Where(f => !lineConceptIds.Contains(f.FinancialConceptId)));
            if (!readIssuerIds.Contains(issuer.Key))
            {
                read.AddRange(line);
                continue;
            }
            var issuerHistory = history[issuer.Key].ToList();
            var reading = OwnersProfitSign.Read(
                line,
                ownersIdSet,
                [.. issuerHistory.Where(f => evidenceConcepts.ContainsKey(f.FinancialConceptId))],
                evidenceConcepts,
                [.. issuerHistory.Where(f => ownersIdSet.Contains(f.FinancialConceptId))]
            );
            read.AddRange(reading.Facts);
            negated.UnionWith(reading.Negated);
            withheld.UnionWith(reading.WithheldPeriods);
        }
        return negated.Count == 0 && withheld.Count == 0
            ? unchanged
            : new OwnersProfitSign.Reading(read, negated, withheld);
    }
}
