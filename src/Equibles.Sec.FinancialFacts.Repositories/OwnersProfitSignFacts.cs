using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

/// <summary>
/// An issuer's net income line read with the owners' profit sign its filings prove
/// (<see cref="OwnersProfitSign"/>). One loader for the fact tools, the statement tool and a
/// host's own readers, so a period reads one sign on every surface.
/// </summary>
public static class OwnersProfitSignFacts
{
    /// <summary>
    /// <paramref name="facts"/> with the facts of <paramref name="lineConceptIds"/> (the net
    /// income line) read by <see cref="OwnersProfitSign.Read"/> against the issuer's whole
    /// history of evidence; every other fact passes through untouched. Evidence is loaded only
    /// for a foreign issuer's line.
    /// </summary>
    public static async Task<OwnersProfitSign.Reading> Read(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        Guid issuerId,
        IReadOnlyCollection<FinancialFact> facts,
        IReadOnlySet<Guid> lineConceptIds,
        CancellationToken cancellationToken = default
    )
    {
        var line = facts.Where(f => lineConceptIds.Contains(f.FinancialConceptId)).ToList();
        var unchanged = new OwnersProfitSign.Reading(
            [.. facts],
            new HashSet<FinancialFact>(),
            new HashSet<(DateOnly, DateOnly, string)>()
        );
        if (!OwnersProfitSign.MayApply(line))
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
            .ToHashSet();
        var evidenceConcepts = concepts
            .Where(c => !OwnersProfitSign.IsOwnersProfit(c.Taxonomy, c.Tag))
            .ToDictionary(c => c.Id, c => (c.Taxonomy, c.Tag));
        if (ownersIds.Count == 0 || evidenceConcepts.Count == 0)
            return unchanged;
        var evidenceIds = evidenceConcepts.Keys.ToList();
        var loadedIds = evidenceIds.Concat(ownersIds).ToList();
        var loaded = await financialFactRepository
            .GetConsolidatedByIssuerId(issuerId)
            .Where(f => loadedIds.Contains(f.FinancialConceptId))
            .ToListAsync(cancellationToken);
        // The whole owners' history decides which units are proven mis-signed, so a read of one
        // period withholds what the fact series withholds.
        var reading = OwnersProfitSign.Read(
            line,
            ownersIds,
            [.. loaded.Where(f => evidenceConcepts.ContainsKey(f.FinancialConceptId))],
            evidenceConcepts,
            [.. loaded.Where(f => ownersIds.Contains(f.FinancialConceptId))]
        );
        if (reading.Negated.Count == 0 && reading.WithheldPeriods.Count == 0)
            return unchanged;
        return reading with
        {
            Facts =
            [
                .. facts.Where(f => !lineConceptIds.Contains(f.FinancialConceptId)),
                .. reading.Facts,
            ],
        };
    }
}
