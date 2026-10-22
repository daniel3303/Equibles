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
            var counts = await CoverPageCounts(batch, conceptIds, cancellationToken);
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

    // Each filing's cover-page count: its consolidated fact, or, when it states none, the sum of
    // its per-class counts on one class-of-stock axis, each class once. Multi-class issuers state
    // only the per-class counts; without their sum a spin-off factor at such an issuer has no
    // witness and restates its share counts as if it were a split.
    private async Task<ILookup<Guid, ShareCountObservation>> CoverPageCounts(
        Guid[] issuerIds,
        List<Guid> conceptIds,
        CancellationToken cancellationToken
    )
    {
        var consolidated = await _factRepository
            .GetConsolidatedByIssuerIds(issuerIds)
            .Where(fact => conceptIds.Contains(fact.FinancialConceptId) && fact.Unit == SharesUnit)
            .Select(fact => new
            {
                fact.EquityIssuerId,
                fact.PeriodEnd,
                fact.FiledDate,
                fact.AccessionNumber,
                fact.Value,
            })
            .ToListAsync(cancellationToken);
        var perClass = await _factRepository
            .GetByIssuerIds(issuerIds)
            .Where(fact =>
                conceptIds.Contains(fact.FinancialConceptId)
                && fact.Unit == SharesUnit
                && fact.Value >= 0
                && fact.Dimensions.Count == 1
            )
            .SelectMany(
                fact => fact.Dimensions,
                (fact, dimension) =>
                    new
                    {
                        fact.EquityIssuerId,
                        fact.PeriodEnd,
                        fact.FiledDate,
                        fact.AccessionNumber,
                        fact.Value,
                        dimension.Axis,
                        dimension.Member,
                    }
            )
            .Where(fact => SharesOutstandingProvider.ClassOfStockAxes.Contains(fact.Axis))
            .ToListAsync(cancellationToken);

        var statedTotals = consolidated
            .Select(fact => (fact.EquityIssuerId, fact.AccessionNumber, fact.PeriodEnd))
            .ToHashSet();
        var classSums = new List<(Guid EquityIssuerId, ShareCountObservation Count)>();
        foreach (
            var filing in perClass.GroupBy(fact =>
                (fact.EquityIssuerId, fact.AccessionNumber, fact.Axis)
            )
        )
        {
            // The filing's latest date, and only when it states every class the filing names: a
            // class stated only at another date would silently drop out of the total, the rule
            // SharesOutstandingProvider applies to the same cover-page facts.
            var asOf = filing.Max(fact => fact.PeriodEnd);
            var current = filing.Where(fact => fact.PeriodEnd == asOf).ToList();
            var members = current.Select(fact => fact.Member).ToHashSet(StringComparer.Ordinal);
            // A non-class member makes the sum ambiguous: filers tag their only common class as
            // us-gaap:CommonStockMember beside preferred or note members, so dropping it as a roll-up
            // leaves an unchanged preferred count that would contradict a real split.
            if (
                filing.Any(fact => !members.Contains(fact.Member))
                || members.Any(SharesOutstandingProvider.NonClassMembers.Contains)
                || statedTotals.Contains(
                    (filing.Key.EquityIssuerId, filing.Key.AccessionNumber, asOf)
                )
            )
                continue;
            classSums.Add(
                (
                    filing.Key.EquityIssuerId,
                    new ShareCountObservation(
                        asOf,
                        current.Max(fact => fact.FiledDate),
                        current
                            .GroupBy(fact => fact.Member, StringComparer.Ordinal)
                            .Sum(member => member.First().Value)
                    )
                )
            );
        }
        return consolidated
            .Select(fact =>
                (
                    fact.EquityIssuerId,
                    Count: new ShareCountObservation(fact.PeriodEnd, fact.FiledDate, fact.Value)
                )
            )
            .Concat(classSums)
            .ToLookup(entry => entry.EquityIssuerId, entry => entry.Count);
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
