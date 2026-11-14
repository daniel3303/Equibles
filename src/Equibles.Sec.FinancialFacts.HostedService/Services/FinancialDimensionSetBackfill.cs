using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Finite backfill: copies the dimension combinations stored per fact
/// (<see cref="FinancialFactDimension"/>) into <see cref="FinancialDimensionSet"/>, for facts
/// written before the set table existed or by a host still running that release. It retires with
/// the per-fact table.
/// </summary>
[Service]
public class FinancialDimensionSetBackfill
{
    private const int IssuerBatchSize = 100;
    private const int FactBatchSize = 2000;

    private readonly EquityIssuerRepository _issuerRepository;
    private readonly FinancialFactRepository _factRepository;
    private readonly FinancialFactDimensionRepository _dimensionRepository;
    private readonly FinancialDimensionSetRepository _setRepository;
    private readonly FinancialDimensionSetWriter _writer;

    public FinancialDimensionSetBackfill(
        EquityIssuerRepository issuerRepository,
        FinancialFactRepository factRepository,
        FinancialFactDimensionRepository dimensionRepository,
        FinancialDimensionSetRepository setRepository,
        FinancialDimensionSetWriter writer
    )
    {
        _issuerRepository = issuerRepository;
        _factRepository = factRepository;
        _dimensionRepository = dimensionRepository;
        _setRepository = setRepository;
        _writer = writer;
    }

    /// <summary>Walks every issuer in id order; a pass over fully backfilled facts only reads.</summary>
    public async Task<FinancialDimensionSetBackfillPass> Run(CancellationToken cancellationToken)
    {
        var issuerIds = await _issuerRepository
            .GetAll()
            .OrderBy(issuer => issuer.Id)
            .Select(issuer => issuer.Id)
            .ToListAsync(cancellationToken);
        var added = 0;
        var unresolved = 0;
        foreach (var batch in issuerIds.Chunk(IssuerBatchSize))
        {
            var missing = _factRepository
                .GetAll()
                .Where(fact =>
                    batch.Contains(fact.EquityIssuerId)
                    && fact.DimensionsKey != ""
                    && !_setRepository.GetAll().Any(set => set.Key == fact.DimensionsKey)
                );
            // One fact per key states the whole set. The id is compared as text because
            // PostgreSQL before 18 has no min(uuid).
            var representatives = await missing
                .GroupBy(fact => fact.DimensionsKey)
                .Select(group => new
                {
                    Key = group.Key,
                    FactId = group.Min(fact => fact.Id.ToString()),
                })
                .ToListAsync(cancellationToken);
            if (representatives.Count == 0)
                continue;

            var verified = await Verified(
                representatives.Select(row => (row.Key, Guid.Parse(row.FactId))).ToList(),
                cancellationToken
            );
            // A fact whose rows were cut short cannot state its set; another fact with the same
            // key can.
            var open = representatives
                .Select(row => row.Key)
                .Where(key => !verified.ContainsKey(key))
                .ToArray();
            if (open.Length > 0)
            {
                var others = await missing
                    .Where(fact => open.Contains(fact.DimensionsKey))
                    .Select(fact => new { fact.DimensionsKey, fact.Id })
                    .ToListAsync(cancellationToken);
                var recovered = await Verified(
                    others.Select(row => (row.DimensionsKey, row.Id)).ToList(),
                    cancellationToken
                );
                foreach (var (key, dimensions) in recovered)
                    verified[key] = dimensions;
                unresolved += open.Count(key => !verified.ContainsKey(key));
            }
            added += await _writer.AddMissing(verified, cancellationToken);
        }
        return new FinancialDimensionSetBackfillPass(added, unresolved);
    }

    /// <summary>The first candidate fact per key whose stored rows hash to that key.</summary>
    private async Task<Dictionary<string, List<ParsedXbrlDimension>>> Verified(
        IReadOnlyCollection<(string Key, Guid FactId)> candidates,
        CancellationToken cancellationToken
    )
    {
        var verified = new Dictionary<string, List<ParsedXbrlDimension>>(StringComparer.Ordinal);
        foreach (var chunk in candidates.Chunk(FactBatchSize))
        {
            var factIds = chunk.Select(candidate => candidate.FactId).ToArray();
            var rows = await _dimensionRepository
                .GetAll()
                .Where(dimension => factIds.Contains(dimension.FinancialFactId))
                .Select(dimension => new
                {
                    dimension.FinancialFactId,
                    dimension.Axis,
                    dimension.Member,
                })
                .ToListAsync(cancellationToken);
            var byFact = rows.ToLookup(
                row => row.FinancialFactId,
                row => new ParsedXbrlDimension { Axis = row.Axis, Member = row.Member }
            );
            foreach (var (key, factId) in chunk)
            {
                if (verified.ContainsKey(key))
                    continue;
                var dimensions = byFact[factId].ToList();
                if (XbrlDimensionsKey.Compute(dimensions) == key)
                    verified[key] = dimensions;
            }
        }
        return verified;
    }
}
