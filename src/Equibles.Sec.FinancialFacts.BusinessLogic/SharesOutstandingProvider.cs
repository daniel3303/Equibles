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
// issuer reports the count only per share class (dimensional facts on a class-of-stock axis,
// no consolidated fact), so GetSummedPerClassSharesOutstanding sums those classes into the entity
// total (#2503).
[Service(ServiceLifetime.Scoped, typeof(ISharesOutstandingProvider))]
public class SharesOutstandingProvider : ISharesOutstandingProvider
{
    private const string SharesUnit = "shares";

    // XBRL axes that mark a per-share-class cover-page count (e.g. Class A vs Class C) as opposed
    // to the consolidated total. Multi-class issuers report dei:EntityCommonStockSharesOutstanding
    // dimensioned on one of these axes and carry no consolidated fact, so the entity total is the
    // sum across the axis members. Domestic filers use the us-gaap statement axis; IFRS filers
    // (20-F) report the same semantic on the ifrs-full share-capital axes — without them a
    // multi-class IFRS filer's per-class counts are invisible and a stale consolidated fact wins.
    private static readonly string[] ClassOfStockAxes =
    [
        "us-gaap:StatementClassOfStockAxis",
        "ifrs-full:ClassesOfShareCapitalAxis",
        "ifrs-full:ClassesOfOrdinarySharesAxis",
    ];

    // A cover-page count this many times smaller than BOTH the issuer's previous cover-page count
    // and the same filing's balance-sheet count is treated as a filing artifact (see
    // IsCollapsedCoverPageCount). Observed artifacts are 10x-1000x off (a dropped digit or a
    // thousands-scaled entry). A genuine reduction this large in one filing window (a reverse
    // split, going private) resolves safely either way: a balance sheet stated on the new share
    // basis agrees with the reduced cover page and the count is kept, while a contradicted one
    // abstains and the price feed's listed-security count stands.
    private const decimal CoverPageCollapseFactor = 5m;

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

    // The current entity share count together with the filing that stated it.
    private sealed record SharesFact(
        long Shares,
        DateOnly Filed,
        DocumentType Form,
        string AccessionNumber
    );

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
    // count on a class-of-stock axis (a single-class filer's consolidated fact is read by
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
    //
    // Null when nothing is on record, or when the latest cover-page count is a filing artifact
    // (see IsCollapsedCoverPageCount): EDGAR abstains rather than propagate a count its own filing
    // contradicts, and the caller's fallback source (the price feed's listed-security count) stands.
    public async Task<long?> GetCurrentSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    ) => (await ResolveCurrentSharesFact(stock, cancellationToken))?.Shares;

    // True when the fact backing GetCurrentSharesOutstanding — the latest consolidated cover-page
    // fact or the latest per-class filing, whichever wins the pick — is a foreign-private-issuer
    // annual form (20-F/40-F). Those cover-page counts are in the issuer's ordinary shares, which
    // are a different unit from the US-listed ADR a price feed quotes; the Yahoo importer uses this
    // to skip reconciling Yahoo's (correct, self-consistent) ADR market cap / shares onto that
    // ordinary base, which would otherwise inflate market cap by the ADR ratio (e.g. Latam Airlines
    // ~2000x), and the financial-facts importer uses it to leave the stored ADR share base alone.
    // Keyed to the same pick so a multi-class 20-F filer (per-class facts only) is recognized, not
    // just one with a consolidated fact. Authoritative — the SEC form, not a ticker/name heuristic.
    public async Task<bool> IsForeignPrivateIssuer(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        var fact = await ResolveCurrentSharesFact(stock, cancellationToken);
        return fact != null
            && (fact.Form == DocumentType.TwentyF || fact.Form == DocumentType.FortyF);
    }

    // The single source of truth for "the issuer's current share count and the filing that stated
    // it", shared by GetCurrentSharesOutstanding and IsForeignPrivateIssuer so the two can never
    // disagree about which fact is authoritative. Callers pair the two accessors on the same
    // stock and the resolution costs several queries, so the result (including an abstention) is
    // memoized per stock for this scoped instance's lifetime — one import scope, single consumer.
    private readonly Dictionary<Guid, SharesFact> _currentFactByStock = [];

    private async Task<SharesFact> ResolveCurrentSharesFact(
        CommonStock stock,
        CancellationToken cancellationToken
    )
    {
        if (_currentFactByStock.TryGetValue(stock.Id, out var cached))
            return cached;

        var resolved = await ResolveCurrentSharesFactUncached(stock, cancellationToken);
        _currentFactByStock[stock.Id] = resolved;
        return resolved;
    }

    private async Task<SharesFact> ResolveCurrentSharesFactUncached(
        CommonStock stock,
        CancellationToken cancellationToken
    )
    {
        var coverPageConceptIds = await ResolveConceptIds(cancellationToken, FactTaxonomy.Dei);
        var consolidated = await GetLatestConsolidated(
            stock,
            coverPageConceptIds,
            cancellationToken
        );
        var perClass = await GetLatestPerClass(stock, coverPageConceptIds, cancellationToken);

        // The more-recently-filed figure wins; a same-filing tie keeps the consolidated total,
        // which is the entity figure directly (#5158).
        if (perClass != null && (consolidated == null || perClass.Filed > consolidated.Filed))
            return perClass;

        if (consolidated != null)
        {
            var collapsed = await IsCollapsedCoverPageCount(
                stock,
                consolidated,
                coverPageConceptIds,
                cancellationToken
            );
            return collapsed ? null : consolidated;
        }

        // No dei cover-page fact on record — fall back to the balance-sheet consolidated count,
        // the best available for an issuer that never reported the authoritative cover-page tag.
        var fallbackConceptIds = await ResolveConceptIds(cancellationToken);
        return await GetLatestConsolidated(stock, fallbackConceptIds, cancellationToken);
    }

    // True when the latest consolidated cover-page count is contradicted as a collapse artifact by
    // BOTH of the filer's own other statements of the same measure: the previous cover-page count
    // (an earlier filing) and the same filing's us-gaap balance-sheet count, each at least
    // CoverPageCollapseFactor times larger. Real filings show this exact shape when the filer drops
    // a digit or types the count in thousands (observed: 36,710 vs 36.4M; 161,489 vs 17.0M;
    // 8,294,933 vs 82.9M) — the artifact then poisons every downstream ratio until the next filing.
    // Requiring both anchors keeps the check one-sided and conservative: a garbage-LARGE
    // balance-sheet figure alone (the mis-scaled inverse artifact) does not fire because history
    // still confirms the cover-page count, and a genuinely tiny issuer (e.g. a wholly-owned
    // subsidiary with 1 share) has a tiny prior count, so it does not fire either.
    private async Task<bool> IsCollapsedCoverPageCount(
        CommonStock stock,
        SharesFact latest,
        IReadOnlyCollection<Guid> coverPageConceptIds,
        CancellationToken cancellationToken
    )
    {
        var collapseThreshold = latest.Shares * CoverPageCollapseFactor;

        var priorCoverPage = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f =>
                coverPageConceptIds.Contains(f.FinancialConceptId)
                && f.Unit == SharesUnit
                && f.FiledDate < latest.Filed
                && f.Value > 0
            )
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.PeriodEnd)
            .Select(f => (decimal?)f.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (priorCoverPage == null || priorCoverPage < collapseThreshold)
            return false;

        // The same filing's balance-sheet count at its most recent as-of date. Same-accession so a
        // later filing can never masquerade as the corroborating anchor.
        var balanceSheetConceptIds = await ResolveConceptIds(
            cancellationToken,
            FactTaxonomy.UsGaap
        );
        if (balanceSheetConceptIds.Count == 0)
            return false;

        var sameFilingBalanceSheet = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f =>
                balanceSheetConceptIds.Contains(f.FinancialConceptId)
                && f.Unit == SharesUnit
                && f.AccessionNumber == latest.AccessionNumber
                && f.Value > 0
            )
            .OrderByDescending(f => f.PeriodEnd)
            .Select(f => (decimal?)f.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return sameFilingBalanceSheet != null && sameFilingBalanceSheet >= collapseThreshold;
    }

    // The latest-filed consolidated (classless) cover-page count and the filing it came from, or
    // null when the issuer has no consolidated fact on record or the count is unrepresentable as
    // Int64.
    private async Task<SharesFact> GetLatestConsolidated(
        CommonStock stock,
        IReadOnlyCollection<Guid> conceptIds,
        CancellationToken cancellationToken
    )
    {
        if (conceptIds.Count == 0)
            return null;

        // The latest filing wins (FiledDate), then the most recent as-of date within it; the value
        // is a whole share count. FromValue round-trips Form back to the cached DocumentType
        // statics, so reference equality holds after materialization.
        var match = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f => conceptIds.Contains(f.FinancialConceptId) && f.Unit == SharesUnit)
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.PeriodEnd)
            .Select(f => new
            {
                f.Value,
                f.FiledDate,
                f.Form,
                f.AccessionNumber,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (match == null)
            return null;

        // A corrupt/typo'd cover-page fact can carry a count that parses but exceeds Int64; the
        // decimal->long cast would throw, crashing the caller. Treat an unrepresentable figure as
        // none on record (null), matching how every other decimal->long cast here is range-checked.
        return match.Value >= long.MinValue && match.Value <= long.MaxValue
            ? new SharesFact((long)match.Value, match.FiledDate, match.Form, match.AccessionNumber)
            : null;
    }

    // The entity total summed across the latest filing's per-class cover-page facts and that
    // filing, or null when the issuer reports no per-class count on a class-of-stock axis or the
    // sum is unrepresentable as Int64.
    private async Task<SharesFact> GetLatestPerClass(
        CommonStock stock,
        IReadOnlyCollection<Guid> conceptIds,
        CancellationToken cancellationToken
    )
    {
        if (conceptIds.Count == 0)
            return null;

        // Per-share-class cover-page facts only: a single explicit dimension, on a class-of-stock
        // axis. A fact dimensioned otherwise (segment/geography), on several axes, or with none is
        // excluded so only genuine per-class counts are summed.
        var perClassFacts = await _financialFactRepository
            .GetByStock(stock)
            .Where(f =>
                conceptIds.Contains(f.FinancialConceptId)
                && f.Unit == SharesUnit
                && f.Dimensions.Count == 1
                && f.Dimensions.Any(d => ClassOfStockAxes.Contains(d.Axis))
            )
            .Include(f => f.Dimensions)
            .ToListAsync(cancellationToken);
        if (perClassFacts.Count == 0)
            return null;

        // Sum across share classes from the latest filing only — pinned to that one accession,
        // as-of date and axis (a filer double-tagging the same classes on two axes must not be
        // double-counted), and grouped by class member so a restated row never double-counts a
        // class.
        var latest = perClassFacts
            .OrderByDescending(f => f.FiledDate)
            .ThenByDescending(f => f.PeriodEnd)
            // Deterministic axis pick when a filer double-tags the same filing's classes on two
            // class axes — without it the pinned axis depends on list order among equal keys.
            .ThenBy(f => Array.IndexOf(ClassOfStockAxes, f.Dimensions[0].Axis))
            .First();

        var total = perClassFacts
            .Where(f =>
                f.AccessionNumber == latest.AccessionNumber
                && f.PeriodEnd == latest.PeriodEnd
                && f.Dimensions[0].Axis == latest.Dimensions[0].Axis
            )
            .GroupBy(f => f.Dimensions[0].Member)
            .Sum(g => g.First().Value);

        // Same range-check: a corrupt per-class count can push the sum past Int64; degrade to null
        // rather than let the decimal->long cast throw.
        return total > 0 && total <= long.MaxValue
            ? new SharesFact((long)total, latest.FiledDate, latest.Form, latest.AccessionNumber)
            : null;
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
