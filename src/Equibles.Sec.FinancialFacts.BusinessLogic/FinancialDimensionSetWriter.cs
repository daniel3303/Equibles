using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>
/// Stores each distinct dimension combination once, under the fingerprint its facts carry in
/// <see cref="FinancialFact.DimensionsKey"/>.
/// </summary>
[Service]
public class FinancialDimensionSetWriter
{
    private const int KeyLookupBatchSize = 2000;

    private readonly FinancialDimensionSetRepository _setRepository;

    public FinancialDimensionSetWriter(FinancialDimensionSetRepository setRepository)
    {
        _setRepository = setRepository;
    }

    /// <summary>
    /// Adds the sets not stored yet and returns how many it added; the consolidated (empty) key
    /// has no set. Every fact with the key reads the set, so one that does not hash to its key is refused.
    /// </summary>
    public async Task<int> AddMissing(
        IReadOnlyDictionary<string, List<ParsedXbrlDimension>> dimensionsByKey,
        CancellationToken cancellationToken
    )
    {
        var keys = dimensionsByKey.Keys.Where(key => key != "").ToArray();
        var stored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var batch in keys.Chunk(KeyLookupBatchSize))
        {
            stored.UnionWith(
                await _setRepository
                    .GetAll()
                    .Where(set => batch.Contains(set.Key))
                    .Select(set => set.Key)
                    .ToListAsync(cancellationToken)
            );
        }

        var missing = new List<FinancialDimensionSet>();
        foreach (var key in keys.Where(key => !stored.Contains(key)))
        {
            var dimensions = dimensionsByKey[key];
            if (XbrlDimensionsKey.Compute(dimensions) != key)
                throw new InvalidOperationException(
                    $"Dimension set {key} does not hash to its key."
                );
            missing.Add(
                new FinancialDimensionSet
                {
                    Key = key,
                    Members = dimensions
                        .Select(dimension => (dimension.Axis, dimension.Member))
                        .Distinct()
                        .Select(pair => new FinancialDimensionSetMember
                        {
                            DimensionSetKey = key,
                            Axis = pair.Axis,
                            Member = pair.Member,
                        })
                        .ToList(),
                }
            );
        }
        if (missing.Count > 0)
            await _setRepository.AddIgnoringExisting(missing, cancellationToken);
        return missing.Count;
    }
}
