using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Splits;

/// <summary>
/// Recurring reconciliation: judges every captured split against the issuer's cover-page share
/// counts (<see cref="SplitShareCountEvidence"/>), so a late filing or a revised ratio converges on the next pass.
/// </summary>
[Service]
public class SplitShareCountEvidenceReconciler
{
    private const int IssuerBatchSize = 500;
    private const string SharesUnit = "shares";

    private readonly StockSplitRepository _splitRepository;
    private readonly FinancialFactRepository _factRepository;
    private readonly FinancialConceptRepository _conceptRepository;

    public SplitShareCountEvidenceReconciler(
        StockSplitRepository splitRepository,
        FinancialFactRepository factRepository,
        FinancialConceptRepository conceptRepository
    )
    {
        _splitRepository = splitRepository;
        _factRepository = factRepository;
        _conceptRepository = conceptRepository;
    }

    /// <summary>Judges every issuer in id order; an issuer the database refuses is reported, not fatal.</summary>
    public async Task<SplitShareCountEvidencePass> Reconcile(CancellationToken cancellationToken)
    {
        var conceptIds = await CoverPageConceptIds(cancellationToken);
        var issuerIds = await _splitRepository
            .GetAll()
            .Select(split => split.EquityIssuerId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
        var changed = 0;
        var failures = new List<(Guid IssuerId, Exception Error)>();
        foreach (var batch in issuerIds.Chunk(IssuerBatchSize))
        {
            var splits = await _splitRepository
                .GetAll()
                .AsNoTracking()
                .Where(split => batch.Contains(split.EquityIssuerId))
                .ToListAsync(cancellationToken);
            var counts = (
                await _factRepository
                    .GetConsolidatedByIssuerIds(batch)
                    .Where(fact =>
                        conceptIds.Contains(fact.FinancialConceptId) && fact.Unit == SharesUnit
                    )
                    .Select(fact => new
                    {
                        fact.EquityIssuerId,
                        fact.PeriodEnd,
                        fact.FiledDate,
                        fact.Value,
                    })
                    .ToListAsync(cancellationToken)
            ).ToLookup(
                fact => fact.EquityIssuerId,
                fact => new ShareCountObservation(fact.PeriodEnd, fact.FiledDate, fact.Value)
            );
            var now = DateTime.UtcNow;
            foreach (
                var issuerSplits in splits
                    .GroupBy(split => split.EquityIssuerId)
                    .OrderBy(group => group.Key)
            )
            {
                try
                {
                    changed += await Record(
                        SplitShareCountEvidence.Evaluate(
                            issuerSplits.ToList(),
                            counts[issuerSplits.Key].ToList()
                        ),
                        now,
                        cancellationToken
                    );
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One row a database guard refuses must not leave every later issuer unjudged.
                    failures.Add((issuerSplits.Key, exception));
                }
            }
        }
        return new SplitShareCountEvidencePass(changed, failures);
    }

    private async Task<int> Record(
        IEnumerable<SplitShareCountVerdict> verdicts,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var changed = 0;
        foreach (var verdict in verdicts)
        {
            if (
                verdict.Evidence == verdict.Split.ShareCountEvidence
                && verdict.AsOf == verdict.Split.ShareCountEvidenceAsOf
            )
                continue;
            changed += await _splitRepository.SetShareCountEvidence(
                verdict.Split,
                verdict.Evidence,
                verdict.AsOf,
                now,
                cancellationToken
            );
        }
        return changed;
    }

    // The dei cover-page count: a literal count on its as-of date, never restated by the filer.
    private async Task<List<Guid>> CoverPageConceptIds(CancellationToken cancellationToken)
    {
        if (!FinancialConceptAliases.TryResolve("shares-outstanding", out var concepts))
            return [];
        var tags = concepts
            .Where(concept => concept.Taxonomy == FactTaxonomy.Dei)
            .Select(concept => concept.Tag)
            .ToList();
        return await _conceptRepository
            .GetMatching([FactTaxonomy.Dei], tags)
            .Select(concept => concept.Id)
            .ToListAsync(cancellationToken);
    }
}
