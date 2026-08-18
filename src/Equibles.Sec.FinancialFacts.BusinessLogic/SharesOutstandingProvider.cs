using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
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

    // A cover-page count this many times smaller than BOTH the issuer's recent cover-page history
    // and the same filing's balance-sheet count is treated as a filing artifact (see
    // TryResolveCollapseCorrection). Observed artifacts are 10x-1000x off (a dropped digit or a
    // thousands-scaled entry). A genuine reduction this large in one filing window (a reverse
    // split, going private) resolves safely either way: a balance sheet stated on the new share
    // basis agrees with the reduced cover page and the count is kept, while a contradicted one
    // is replaced by the balance-sheet count that contradicted it.
    private const decimal CoverPageCollapseFactor = 5m;

    // Members observed on the class-of-stock axes that are not share classes by taxonomy
    // definition, excluded from any per-class sum: the consolidated roll-up (summing it with the
    // classes it totals double-counts the company), treasury shares (issued but not outstanding),
    // and the ADS listing (a different unit from the ordinary-share classes beside it).
    private static readonly string[] NonClassMembers =
    [
        "us-gaap:CommonStockMember",
        "us-gaap:TreasuryStockMember",
        "us-gaap:TreasuryStockCommonMember",
        "dei:AdrMember",
    ];

    // How far back the collapse check's history anchor looks. The anchor asks whether the issuer
    // was RECENTLY much bigger, and it must survive the artifact being repeated (Air Lease filed
    // 200 on two consecutive cover pages, so "the previous fact" was the artifact itself) — hence
    // the maximum over a window rather than the single most recent fact. Two years spans an
    // annual filer with a missed year while keeping some ancient mis-scaled fact from posing as
    // recent history.
    private const int CollapseHistoryWindowDays = 730;

    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;
    private readonly StockSplitRepository _stockSplitRepository;

    public SharesOutstandingProvider(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository,
        StockSplitRepository stockSplitRepository
    )
    {
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
        _stockSplitRepository = stockSplitRepository;
    }

    // The current entity share count together with the filing that stated it. AsOf is the
    // instant the count was stated for (the fact's PeriodEnd), which trails the filed date —
    // a 10-Q cover states "shares outstanding as of <a few days before filing>".
    private sealed record SharesFact(
        long Shares,
        DateOnly AsOf,
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
    // When the latest cover-page count is a filing artifact (see TryResolveCollapseCorrection),
    // the same filing's balance-sheet count — the figure that proved the artifact — is returned in
    // its place, so a repeated artifact cannot pin the stored count to garbage while every other
    // statement in the filing carries the real figure. Null when nothing is on record.
    public async Task<long?> GetCurrentSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        var fact = await ResolveCurrentSharesFact(stock, cancellationToken);
        if (fact == null)
            return null;
        return await RestateToCurrentSplitBasis(stock, fact, cancellationToken);
    }

    // A cover-page count is stated as-of a date inside its filing window, and the NEXT cover page
    // is typically a quarter away — so a split effective in between leaves this figure on the
    // pre-split basis for months (BYND's 1-for-30 kept every market-cap and ownership surface 30x
    // wrong until its next 10-Q). Restate the count onto today's basis with the captured splits,
    // the same read-time contract every other share-count consumer applies (SplitAdjustment):
    // only splits already effective apply (never an announced future one), and the strict
    // after-AsOf comparison makes the first post-split cover page a natural no-op — no flag to
    // clear, no double application. Scoped to the primary price series (exact primary ticker plus
    // legacy null attribution): the entity total moves with the issuer's common stock, and a
    // split attributed only to a sibling listing must not rescale it.
    private async Task<long> RestateToCurrentSplitBasis(
        CommonStock stock,
        SharesFact fact,
        CancellationToken cancellationToken
    )
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var splits = await _stockSplitRepository
            .GetEffectiveByStock(stock.Id, today)
            .Where(split =>
                split.PriceSeriesTicker == null || split.PriceSeriesTicker == stock.Ticker
            )
            .ToListAsync(cancellationToken);

        var factor = SplitAdjustment.ShareCountFactor(fact.AsOf, splits);
        // Defensive: a corrupt split row (zero/negative numerator) zeroes the factor, and a zeroed
        // count is a worse answer than the stale one — leave the count as filed, like every other
        // non-positive-factor fallback in the split-adjustment family.
        return factor <= 0m ? fact.Shares : SplitAdjustment.AdjustShareCount(fact.Shares, factor);
    }

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
            var collapse = await EvaluateCoverPageCollapse(
                stock,
                consolidated,
                coverPageConceptIds,
                cancellationToken
            );
            // Not collapsed → the cover page stands. Collapsed with a grounded correction → the
            // correction. Collapsed without one → abstain, so the caller's fallback source (the
            // price feed's listed-security count) stands and nothing propagates a contested figure.
            return collapse.Collapsed ? collapse.Correction : consolidated;
        }

        // No dei cover-page fact on record — fall back to the balance-sheet consolidated count,
        // the best available for an issuer that never reported the authoritative cover-page tag.
        var fallbackConceptIds = await ResolveConceptIds(cancellationToken);
        return await GetLatestConsolidated(stock, fallbackConceptIds, cancellationToken);
    }

    // Whether the latest consolidated cover-page count is a collapse artifact, and — separately —
    // whether the evidence is strong enough to state the corrected figure. NotCollapsed keeps the
    // cover page; Collapsed with a null Correction abstains (the pre-existing behavior, so the
    // price feed's listed-security count stands); Collapsed with a Correction returns it.
    private sealed record CollapseOutcome(bool Collapsed, SharesFact Correction)
    {
        public static readonly CollapseOutcome NotCollapsed = new(false, null);
        public static readonly CollapseOutcome Abstain = new(true, null);
    }

    // A cover-page count is COLLAPSED when contradicted by BOTH of the filer's own other
    // statements of the same measure: the issuer's recent cover-page history and the same filing's
    // us-gaap balance-sheet count, each at least CoverPageCollapseFactor times larger. Real
    // filings show this shape when the filer drops a digit or types the count in thousands
    // (observed: 36,710 vs 36.4M; 161,489 vs 17.0M; 8,294,933 vs 82.9M; Air Lease's flat 200 vs
    // 112.4M). Requiring both anchors keeps the check conservative: a genuinely tiny issuer (a
    // wholly-owned subsidiary with 1 share) has a tiny history, so it never fires. The history
    // anchor is the MAXIMUM over a recent window, not the single previous fact — AL filed the
    // artifact on two consecutive cover pages, so "the previous fact" was the artifact itself and
    // a most-recent-prior check could never fire again.
    //
    // A collapse is CORRECTED — the balance-sheet count returned as the answer instead of
    // abstaining — only when the evidence is overdetermined, because a wrong correction is a write
    // where abstention wrote nothing:
    //
    //   - the two corroborators must AGREE with each other as same-unit statements (within the
    //     collapse factor). That grounds the correction in two independent agreeing figures — and
    //     kills the trap where a nominal balance-sheet placeholder (1/100/1000 shares) happens to
    //     clear a threshold computed from a count that is itself garbage-small;
    //   - the cover page must sit beyond MaxPlausibleSameUnitRatio of the correction — too far off
    //     to be a statement of the same unit at all (a flat placeholder, a thousands-scaled
    //     entry). Inside that ratio the cover page could be the one honest figure in the filing —
    //     Reliability Inc's cover page says a correct 46.7M while its history AND classless
    //     balance-sheet fact both carry the same 300M mis-tag (the authorized count): 6.4x apart,
    //     two agreeing statements, and both wrong. In that band the collapse stands but the
    //     correction abstains, exactly as the check behaved before corrections existed.
    //
    // Abstention is not free — an issuer the price feed carries no share base for keeps its
    // garbage until the filer corrects itself — which is why the overdetermined cases correct
    // rather than abstain. AL clears both bars (history 112.0M vs balance sheet 112.4M, cover page
    // 562,000x away); RLBY clears neither.
    private async Task<CollapseOutcome> EvaluateCoverPageCollapse(
        CommonStock stock,
        SharesFact latest,
        IReadOnlyCollection<Guid> coverPageConceptIds,
        CancellationToken cancellationToken
    )
    {
        var collapseThreshold = latest.Shares * CoverPageCollapseFactor;
        var historyWindowStart = latest.Filed.AddDays(-CollapseHistoryWindowDays);

        var priorCoverPageMax = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f =>
                coverPageConceptIds.Contains(f.FinancialConceptId)
                && f.Unit == SharesUnit
                && f.FiledDate < latest.Filed
                && f.FiledDate >= historyWindowStart
                && f.Value > 0
            )
            .MaxAsync(f => (decimal?)f.Value, cancellationToken);
        if (priorCoverPageMax == null || priorCoverPageMax < collapseThreshold)
            return CollapseOutcome.NotCollapsed;

        var sameFilingBalanceSheet = await GetSameFilingBalanceSheetCount(
            stock,
            latest.AccessionNumber,
            cancellationToken
        );
        if (sameFilingBalanceSheet == null || sameFilingBalanceSheet < collapseThreshold)
            return CollapseOutcome.NotCollapsed;

        // Corroborator agreement: history and balance sheet must be same-unit statements of the
        // same figure, or the correction has no ground to stand on.
        var larger = Math.Max(priorCoverPageMax.Value, sameFilingBalanceSheet.Value);
        var smaller = Math.Min(priorCoverPageMax.Value, sameFilingBalanceSheet.Value);
        if (larger > smaller * CoverPageCollapseFactor)
            return CollapseOutcome.Abstain;

        // The cover page must be beyond any same-unit reading of the correction. A zero-valued
        // cover-page fact (no count at all) is trivially beyond it — without the explicit branch
        // the threshold arithmetic above degenerates to zero and admits anything.
        var beyondSameUnit =
            latest.Shares <= 0
            || sameFilingBalanceSheet
                >= latest.Shares * (decimal)ShareBasisPlausibility.MaxPlausibleSameUnitRatio;
        if (!beyondSameUnit)
            return CollapseOutcome.Abstain;

        // Same range-check as every other decimal->long cast here; an unrepresentable count
        // degrades to abstention rather than a throw.
        return sameFilingBalanceSheet <= long.MaxValue
            ? new CollapseOutcome(
                true,
                new SharesFact(
                    (long)sameFilingBalanceSheet.Value,
                    latest.AsOf,
                    latest.Filed,
                    latest.Form,
                    latest.AccessionNumber
                )
            )
            : CollapseOutcome.Abstain;
    }

    // The filing's own balance-sheet share count at its most recent as-of date: the consolidated
    // (classless) fact when the filer states one, otherwise the sum of its single-dimension
    // per-class facts on a class-of-stock axis — mirroring the consolidated-then-per-class shape
    // of the cover-page resolution, because filers split the balance-sheet count the same way (Air
    // Lease states it only on us-gaap:StatementClassOfStockAxis, so a consolidated-only read is
    // blind to its real figure). Same-accession so a later filing can never masquerade as the
    // corroborating anchor. Null when the filing states no balance-sheet count at all.
    private async Task<decimal?> GetSameFilingBalanceSheetCount(
        CommonStock stock,
        string accessionNumber,
        CancellationToken cancellationToken
    )
    {
        var balanceSheetConceptIds = await ResolveConceptIds(
            cancellationToken,
            FactTaxonomy.UsGaap
        );
        if (balanceSheetConceptIds.Count == 0)
            return null;

        var consolidated = await _financialFactRepository
            .GetConsolidatedByStock(stock)
            .Where(f =>
                balanceSheetConceptIds.Contains(f.FinancialConceptId)
                && f.Unit == SharesUnit
                && f.AccessionNumber == accessionNumber
                && f.Value > 0
            )
            .OrderByDescending(f => f.PeriodEnd)
            .Select(f => (decimal?)f.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (consolidated != null)
            return consolidated;

        // Per-class facts, pinned the way GetLatestPerClass pins the cover-page sum: one axis and
        // one as-of date within the accession, grouped by class member so a restated row never
        // double-counts a class. Zero-valued members (a class with nothing outstanding) are kept —
        // they simply add nothing — while a negative row is excluded rather than silently
        // subtracted. Members that are not a share class by taxonomy definition are excluded too:
        // filers put the consolidated roll-up, treasury shares, and the ADS listing on the same
        // axis, and summing any of those with the real classes double-counts or mixes units.
        var perClassFacts = await _financialFactRepository
            .GetByStock(stock)
            .Where(f =>
                balanceSheetConceptIds.Contains(f.FinancialConceptId)
                && f.Unit == SharesUnit
                && f.AccessionNumber == accessionNumber
                && f.Value >= 0
                && f.Dimensions.Count == 1
                && f.Dimensions.Any(d =>
                    ClassOfStockAxes.Contains(d.Axis) && !NonClassMembers.Contains(d.Member)
                )
            )
            .Include(f => f.Dimensions)
            .ToListAsync(cancellationToken);
        if (perClassFacts.Count == 0)
            return null;

        var latest = perClassFacts
            .OrderByDescending(f => f.PeriodEnd)
            .ThenBy(f => Array.IndexOf(ClassOfStockAxes, f.Dimensions[0].Axis))
            .First();

        var total = perClassFacts
            .Where(f =>
                f.PeriodEnd == latest.PeriodEnd && f.Dimensions[0].Axis == latest.Dimensions[0].Axis
            )
            .GroupBy(f => f.Dimensions[0].Member)
            .Sum(g => g.First().Value);

        return total > 0 ? total : null;
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
                f.PeriodEnd,
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
            ? new SharesFact(
                (long)match.Value,
                match.PeriodEnd,
                match.FiledDate,
                match.Form,
                match.AccessionNumber
            )
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
            ? new SharesFact(
                (long)total,
                latest.PeriodEnd,
                latest.FiledDate,
                latest.Form,
                latest.AccessionNumber
            )
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
