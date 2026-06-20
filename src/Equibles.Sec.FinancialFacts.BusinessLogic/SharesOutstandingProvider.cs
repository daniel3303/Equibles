using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

// Resolves a stock's common shares outstanding from the authoritative SEC cover-page tag
// (dei:EntityCommonStockSharesOutstanding) the financial-facts importer already ingests, rather
// than the per-share-class figure Yahoo returns (which understates multi-class issuers ~2x and
// lags corporate actions like reverse splits). A single-class issuer reports a consolidated fact,
// read by GetReportedSharesOutstanding, giving the current entity total (#3575). A multi-class
// issuer reports the count only per share class (dimensional facts on the class-of-stock axis,
// no consolidated fact), so GetSummedPerClassSharesOutstanding sums those classes into the entity
// total (#2503).
[Service(ServiceLifetime.Scoped, typeof(ISharesOutstandingProvider))]
public class SharesOutstandingProvider : ISharesOutstandingProvider
{
    private const string SharesUnit = "shares";

    // XBRL axis that distinguishes a per-share-class cover-page count (e.g. Class A vs Class C)
    // from the consolidated total. Multi-class issuers report
    // dei:EntityCommonStockSharesOutstanding dimensioned on this axis and carry no consolidated
    // fact, so the entity total is the sum across its members.
    private const string ClassOfStockAxis = "us-gaap:StatementClassOfStockAxis";

    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;

    public SharesOutstandingProvider(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository
    )
    {
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
    }

    // The shares on the most-recently-filed consolidated cover-page fact, or null when the issuer
    // has none on record (e.g. a multi-class filer that reports the count only per share class).
    public async Task<long?> GetReportedSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        var conceptIds = await ResolveConceptIds(cancellationToken);
        if (conceptIds.Count == 0)
            return null;

        // The latest filing wins (FiledDate), then the most recent as-of date within it; the value
        // is a whole share count.
        var value = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f => conceptIds.Contains(f.FinancialConceptId) && f.Unit == SharesUnit)
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.PeriodEnd)
            .Select(f => (decimal?)f.Value)
            .FirstOrDefaultAsync(cancellationToken);

        // A corrupt/typo'd cover-page fact can carry a count that parses but exceeds Int64; the
        // decimal->long cast would throw, crashing the caller. Treat an unrepresentable figure as
        // none on record (null), matching how every other decimal->long cast here is range-checked.
        return value.HasValue && value.Value >= long.MinValue && value.Value <= long.MaxValue
            ? (long)value.Value
            : (long?)null;
    }

    // The entity-wide share count for a multi-class issuer, summed across its share classes from
    // the latest filing's per-class cover-page facts, or null when the issuer reports no per-class
    // count on the class-of-stock axis (a single-class filer's consolidated fact is read by
    // GetReportedSharesOutstanding instead). Sourced straight from the issuer's per-class cover-page
    // tags — no heuristic, no MarketCap / Price shortcut.
    public async Task<long?> GetSummedPerClassSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        var conceptIds = await ResolveConceptIds(cancellationToken);
        if (conceptIds.Count == 0)
            return null;

        // Per-share-class cover-page facts only: a single explicit dimension, on the class-of-stock
        // axis. A fact dimensioned otherwise (segment/geography), on several axes, or with none is
        // excluded so only genuine per-class counts are summed.
        var perClassFacts = await _financialFactRepository
            .GetByStock(stock)
            .Where(f =>
                conceptIds.Contains(f.FinancialConceptId)
                && f.Unit == SharesUnit
                && f.Dimensions.Count == 1
                && f.Dimensions.Any(d => d.Axis == ClassOfStockAxis)
            )
            .Include(f => f.Dimensions)
            .ToListAsync(cancellationToken);
        if (perClassFacts.Count == 0)
            return null;

        // Sum across share classes from the latest filing only — pinned to that one accession and
        // as-of date so classes are never mixed across filings, and grouped by class member so a
        // restated row never double-counts a class.
        var latest = perClassFacts
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.PeriodEnd)
            .First();

        var total = perClassFacts
            .Where(f =>
                f.AccessionNumber == latest.AccessionNumber && f.PeriodEnd == latest.PeriodEnd
            )
            .GroupBy(f => f.Dimensions[0].Member)
            .Sum(g => g.First().Value);

        // Same range-check: a corrupt per-class count can push the sum past Int64; degrade to null
        // rather than let the decimal->long cast throw.
        return total > 0 && total <= long.MaxValue ? (long)total : (long?)null;
    }

    // The financial-concept ids the "shares-outstanding" alias resolves to, or an empty list when
    // the alias is unmapped or no matching concept has been ingested yet.
    private async Task<List<Guid>> ResolveConceptIds(CancellationToken cancellationToken)
    {
        if (!FinancialConceptAliases.TryResolve("shares-outstanding", out var refs))
            return [];

        var taxonomies = refs.Select(r => r.Taxonomy).Distinct().ToList();
        var tags = refs.Select(r => r.Tag).ToList();
        return await _financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }
}
