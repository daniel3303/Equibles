using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

// Resolves a stock's common shares outstanding from the authoritative SEC cover-page tag
// (dei:EntityCommonStockSharesOutstanding) the financial-facts importer already ingests, rather
// than the per-share-class figure Yahoo returns (which understates multi-class issuers ~2x and
// lags corporate actions like reverse splits). Consolidated facts only: a multi-class issuer
// reports the count only per share class, so it has no consolidated fact and yields null here —
// the entity total is summed across classes separately (#2503). A single-class issuer's latest
// filing gives the current entity total (#3575).
[Service]
public class SharesOutstandingProvider
{
    private const string SharesUnit = "shares";

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
        if (!FinancialConceptAliases.TryResolve("shares-outstanding", out var refs))
            return null;

        var taxonomies = refs.Select(r => r.Taxonomy).Distinct().ToList();
        var tags = refs.Select(r => r.Tag).ToList();
        var conceptIds = await _financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
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

        return value.HasValue ? (long)value.Value : (long?)null;
    }
}
