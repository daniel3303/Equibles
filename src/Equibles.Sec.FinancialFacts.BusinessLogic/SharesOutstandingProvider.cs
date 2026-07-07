using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
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
    ) =>
        (
            await GetLatestConsolidated(
                stock,
                await ResolveConceptIds(cancellationToken),
                cancellationToken
            )
        )?.Shares;

    // The entity-wide share count for a multi-class issuer, summed across its share classes from
    // the latest filing's per-class cover-page facts, or null when the issuer reports no per-class
    // count on the class-of-stock axis (a single-class filer's consolidated fact is read by
    // GetReportedSharesOutstanding instead). Sourced straight from the issuer's per-class cover-page
    // tags — no heuristic, no MarketCap / Price shortcut.
    public async Task<long?> GetSummedPerClassSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    ) =>
        (
            await GetLatestPerClass(
                stock,
                await ResolveConceptIds(cancellationToken),
                cancellationToken
            )
        )?.Shares;

    // The issuer's current entity total. The authoritative source is the dei:EntityCommonStock
    // SharesOutstanding COVER-PAGE figure: the latest consolidated cover-page fact, or — for a
    // multi-class issuer that reports the count only per share class — the sum across those classes.
    // The us-gaap:CommonStockSharesOutstanding BALANCE-SHEET tag the shares-outstanding alias also
    // maps is NOT an entity total: shells and multi-class filers routinely carry a nominal placeholder
    // there (1, 100, 1000 shares) alongside the real per-class cover-page counts, filed the same day.
    // Resolving both tags together let that placeholder win the same-filing tie, pinning
    // SharesOutStanding to 1 and blowing up every ratio built on it (short interest % of shares,
    // market cap, ownership %). So the cover-page (dei) figure is resolved first; the balance-sheet
    // count is used only as a last-resort fallback for an issuer that never reported the dei tag.
    //
    // A dual-class filer (e.g. Mastercard, Visa) can report BOTH a consolidated cover-page fact and
    // per-class ones — its classless series ended years ago when it moved to per-class reporting,
    // leaving a stale consolidated fact alongside current per-class facts — so the figure from the
    // most recent filing wins; a same-filing tie keeps the consolidated total, which is the entity
    // figure directly (#5158).
    public async Task<long?> GetCurrentSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        var coverPageConceptIds = await ResolveConceptIds(cancellationToken, FactTaxonomy.Dei);
        var consolidated = await GetLatestConsolidated(
            stock,
            coverPageConceptIds,
            cancellationToken
        );
        var perClass = await GetLatestPerClass(stock, coverPageConceptIds, cancellationToken);

        var coverPageTotal = PickEntityTotal(consolidated, perClass);
        if (coverPageTotal != null)
            return coverPageTotal;

        // No dei cover-page fact on record — fall back to the balance-sheet consolidated count,
        // the best available for an issuer that never reported the authoritative cover-page tag.
        var fallbackConceptIds = await ResolveConceptIds(cancellationToken);
        return (await GetLatestConsolidated(stock, fallbackConceptIds, cancellationToken))?.Shares;
    }

    // Picks the entity total from the latest consolidated cover-page fact and the latest per-class
    // sum: the more-recently-filed wins, and a same-filing tie keeps the consolidated total (the
    // entity figure directly). Null when neither is present.
    private static long? PickEntityTotal(
        (long Shares, DateOnly Filed)? consolidated,
        (long Shares, DateOnly Filed)? perClass
    )
    {
        if (consolidated == null)
            return perClass?.Shares;
        if (perClass == null)
            return consolidated.Value.Shares;

        return perClass.Value.Filed > consolidated.Value.Filed
            ? perClass.Value.Shares
            : consolidated.Value.Shares;
    }

    // The latest-filed consolidated (classless) cover-page count and the date it was filed, or null
    // when the issuer has no consolidated fact on record or the count is unrepresentable as Int64.
    private async Task<(long Shares, DateOnly Filed)?> GetLatestConsolidated(
        CommonStock stock,
        IReadOnlyCollection<Guid> conceptIds,
        CancellationToken cancellationToken
    )
    {
        if (conceptIds.Count == 0)
            return null;

        // The latest filing wins (FiledDate), then the most recent as-of date within it; the value
        // is a whole share count.
        var match = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f => conceptIds.Contains(f.FinancialConceptId) && f.Unit == SharesUnit)
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.PeriodEnd)
            .Select(f => new { f.Value, f.FiledDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (match == null)
            return null;

        // A corrupt/typo'd cover-page fact can carry a count that parses but exceeds Int64; the
        // decimal->long cast would throw, crashing the caller. Treat an unrepresentable figure as
        // none on record (null), matching how every other decimal->long cast here is range-checked.
        return match.Value >= long.MinValue && match.Value <= long.MaxValue
            ? ((long)match.Value, match.FiledDate)
            : ((long, DateOnly)?)null;
    }

    // The entity total summed across the latest filing's per-class cover-page facts and that
    // filing's filed date, or null when the issuer reports no per-class count on the class-of-stock
    // axis or the sum is unrepresentable as Int64.
    private async Task<(long Shares, DateOnly Filed)?> GetLatestPerClass(
        CommonStock stock,
        IReadOnlyCollection<Guid> conceptIds,
        CancellationToken cancellationToken
    )
    {
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
        return total > 0 && total <= long.MaxValue
            ? ((long)total, latest.FiledDate)
            : ((long, DateOnly)?)null;
    }

    // True when the latest-filed consolidated shares fact — the one GetReportedSharesOutstanding
    // reconciles against — was filed on a foreign-private-issuer annual form (20-F/40-F). Those
    // cover-page counts are in the issuer's ordinary shares, which are a different unit from the
    // US-listed ADR a price feed quotes; the Yahoo importer uses this to skip reconciling Yahoo's
    // (correct, self-consistent) ADR market cap / shares onto that ordinary base, which would
    // otherwise inflate market cap by the ADR ratio (e.g. Latam Airlines ~2000x). Authoritative —
    // keyed off the SEC form, not a ticker/name heuristic.
    public async Task<bool> IsForeignPrivateIssuer(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        var conceptIds = await ResolveConceptIds(cancellationToken);
        if (conceptIds.Count == 0)
            return false;

        // Same fact selection as GetReportedSharesOutstanding (latest FiledDate, then PeriodEnd),
        // so the gate matches the count that would be reconciled. FromValue round-trips Form back to
        // the cached DocumentType statics, so reference equality holds after materialization.
        var form = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f => conceptIds.Contains(f.FinancialConceptId) && f.Unit == SharesUnit)
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.PeriodEnd)
            .Select(f => f.Form)
            .FirstOrDefaultAsync(cancellationToken);

        return form == DocumentType.TwentyF || form == DocumentType.FortyF;
    }

    // The financial-concept ids the "shares-outstanding" alias resolves to, or an empty list when
    // the alias is unmapped or no matching concept has been ingested yet. Pass a taxonomy to narrow
    // to that source: FactTaxonomy.Dei isolates the authoritative EntityCommonStockSharesOutstanding
    // cover-page tag from the us-gaap CommonStockSharesOutstanding balance-sheet tag the alias also
    // maps.
    private async Task<List<Guid>> ResolveConceptIds(
        CancellationToken cancellationToken,
        FactTaxonomy? taxonomy = null
    )
    {
        if (!FinancialConceptAliases.TryResolve("shares-outstanding", out var refs))
            return [];

        IReadOnlyList<FinancialConceptAliases.ConceptRef> selected =
            taxonomy == null ? refs : refs.Where(r => r.Taxonomy == taxonomy.Value).ToList();
        if (selected.Count == 0)
            return [];

        var taxonomies = selected.Select(r => r.Taxonomy).Distinct().ToList();
        var tags = selected.Select(r => r.Tag).ToList();
        return await _financialConceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }
}
