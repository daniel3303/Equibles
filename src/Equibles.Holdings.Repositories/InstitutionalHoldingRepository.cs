using System.Linq.Expressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Extensions;
using Equibles.Holdings.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class InstitutionalHoldingRepository : BaseRepository<InstitutionalHolding>
{
    public InstitutionalHoldingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    // 13F-only filter shared by the per-stock and per-holder 13F surfaces. Schedule 13D/G rows
    // live in this same table (distinguished by FilingType); excluding them keeps a filer's 13F
    // holding and its 13D/G stake from both rendering and double-counting ownership (GH-4449).
    private static readonly Expression<Func<InstitutionalHolding, bool>> Is13F = h =>
        h.FilingType == FilingType.Form13F;

    public IQueryable<InstitutionalHolding> GetByStock(CommonStock stock, DateOnly reportDate)
    {
        return GetAll().Where(h => h.CommonStockId == stock.Id && h.ReportDate == reportDate);
    }

    // Same stock/date filter as GetByStock, with the InstitutionalHolder navigation eagerly
    // loaded for callers that read holder fields (name) while aggregating or rendering rows.
    public IQueryable<InstitutionalHolding> GetByStockWithHolder(
        CommonStock stock,
        DateOnly reportDate
    )
    {
        return GetByStock(stock, reportDate).Include(h => h.InstitutionalHolder);
    }

    // 13F-only holdings of one stock at a quarter end. Mirrors Get13FByHolder: a holder can
    // file a Schedule 13D/G whose daily event ReportDate coincides with a 13F quarter end and
    // shares this table, so a per-stock institutional-holders list must exclude it or the
    // filer's 13F holding and its 13D/G stake both render as separate rows, double-counting
    // the filer's ownership (GH-4449).
    public IQueryable<InstitutionalHolding> Get13FByStock(CommonStock stock, DateOnly reportDate)
    {
        return GetByStock(stock, reportDate).Where(Is13F);
    }

    // Same stock/date 13F-only filter as Get13FByStock, with the InstitutionalHolder navigation
    // eagerly loaded for callers that render holder fields while aggregating rows.
    public IQueryable<InstitutionalHolding> Get13FByStockWithHolder(
        CommonStock stock,
        DateOnly reportDate
    )
    {
        return Get13FByStock(stock, reportDate).Include(h => h.InstitutionalHolder);
    }

    public IQueryable<InstitutionalHolding> GetByHolder(
        InstitutionalHolder holder,
        DateOnly reportDate
    )
    {
        return GetAll()
            .Where(h => h.InstitutionalHolderId == holder.Id && h.ReportDate == reportDate);
    }

    // Same holder/date filter as GetByHolder, with the CommonStock navigation eagerly loaded
    // for callers that read stock fields (ticker/name) while projecting or rendering rows.
    public IQueryable<InstitutionalHolding> GetByHolderWithStock(
        InstitutionalHolder holder,
        DateOnly reportDate
    )
    {
        return GetByHolder(holder, reportDate).Include(h => h.CommonStock);
    }

    // 13F-only holdings for one holder at a quarter end. A holder can file a Schedule 13D/G
    // whose event ReportDate coincides with a 13F quarter end; that stake shares this table
    // but is not part of the 13F portfolio, so a portfolio summary (reported AUM, sector
    // allocation) must exclude it or the single disclosed position double-counts the AUM.
    public IQueryable<InstitutionalHolding> Get13FByHolder(
        InstitutionalHolder holder,
        DateOnly reportDate
    )
    {
        return GetByHolder(holder, reportDate).Where(Is13F);
    }

    // Same holder/date 13F-only filter as Get13FByHolder, with the CommonStock navigation
    // eagerly loaded for callers that render stock fields (ticker/name) per position.
    public IQueryable<InstitutionalHolding> Get13FByHolderWithStock(
        InstitutionalHolder holder,
        DateOnly reportDate
    )
    {
        return Get13FByHolder(holder, reportDate).Include(h => h.CommonStock);
    }

    public IQueryable<InstitutionalHolding> GetLatestByStock(CommonStock stock)
    {
        var latestDates = GetAll()
            .Where(h => h.CommonStockId == stock.Id)
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new { HolderId = g.Key, LatestDate = g.Max(h => h.ReportDate) });

        return from h in GetAll()
            join ld in latestDates
                on new { HolderId = h.InstitutionalHolderId, Date = h.ReportDate } equals new
                {
                    HolderId = ld.HolderId,
                    Date = ld.LatestDate,
                }
            where h.CommonStockId == stock.Id
            select h;
    }

    public IQueryable<InstitutionalHolding> GetHistoryByStock(CommonStock stock)
    {
        return GetAll().Where(h => h.CommonStockId == stock.Id);
    }

    // 13F-only history of one stock's institutional holders. Schedule 13D/G rows carry a daily
    // event date, not a quarter end, and describe a single disclosed stake; an ownership-trend
    // or report-date series for the stock must exclude them or a 13D/G event date pollutes the
    // quarter axis and its share count inflates the per-quarter total (GH-4449).
    public IQueryable<InstitutionalHolding> Get13FHistoryByStock(CommonStock stock)
    {
        return GetHistoryByStock(stock).Where(Is13F);
    }

    public IQueryable<InstitutionalHolding> GetHistoryByHolder(InstitutionalHolder holder)
    {
        return GetAll().Where(h => h.InstitutionalHolderId == holder.Id);
    }

    // 13F-only view of one holder's history. Schedule 13D/G rows share this table but
    // describe a single stake at an event date, not a portfolio at a quarter end, so
    // portfolio reconstruction (fund scoring, backtests, the smart-money index) must
    // exclude them or a later 13D/G filing replaces the fund's real portfolio.
    public IQueryable<InstitutionalHolding> Get13FHistoryByHolder(InstitutionalHolder holder)
    {
        return GetHistoryByHolder(holder).Where(Is13F);
    }

    // Latest dates first — callers consistently treat index 0 as the newest filing window.
    public IQueryable<DateOnly> GetAvailableReportDates() =>
        GetAll().DistinctReportDatesDescending();

    // Market-wide 13F-only report dates, newest first. Schedule 13D/G rows carry a daily
    // event date, not a quarter end (see Get13FHistoryByHolder); including them makes the
    // "prior" entry the prior day, so a quarter-over-quarter comparison silently degrades
    // into quarter-vs-single-day. Market-wide activity pages resolve their comparison
    // window off this list, so it must stay 13F-only.
    public IQueryable<DateOnly> Get13FAvailableReportDates() =>
        GetAll().Where(Is13F).DistinctReportDatesDescending();

    // The DISTINCT behind Get13FAvailableReportDates scans the whole holdings index (~32M
    // entries) to produce fewer than a hundred quarter-end dates and measures ~28s warm —
    // right at the 30s command timeout, so every surface that resolves its comparison
    // window off the live list (market-wide MCP leaderboards, the activity / export /
    // screener pages) fails on a cold cache. The list gains at most one date per filing
    // day, so a short-lived process-wide cache removes the scan from every request except
    // the first after boot / expiry. Keyed by connection string so parallel databases
    // (test fixtures) never see each other's lists; a null connection string (e.g. the
    // EF InMemory provider) bypasses the cache entirely.
    private static readonly TimeSpan ReportDatesCacheTtl = TimeSpan.FromHours(1);
    private static readonly object ReportDatesCacheLock = new();
    private static readonly Dictionary<
        string,
        (List<DateOnly> Dates, DateTime LoadedUtc)
    > Cached13FReportDates = new();

    // Drops every cached 13F report-date list. Test fixtures that truncate and re-seed the
    // SAME database between tests (Respawn) must call this from their reset, or a list cached
    // by one test leaks into the next.
    public static void ResetProcessWideCaches()
    {
        lock (ReportDatesCacheLock)
        {
            Cached13FReportDates.Clear();
        }
    }

    // Returns a defensive copy so no caller can mutate a shared cached list.
    public async Task<List<DateOnly>> Get13FAvailableReportDatesCached(
        CancellationToken cancellationToken = default
    )
    {
        // GetConnectionString throws on non-relational providers (the EF InMemory tests),
        // which also want no cross-instance caching — so they bypass the cache entirely.
        var cacheKey = DbContext.Database.IsRelational()
            ? DbContext.Database.GetConnectionString()
            : null;
        if (cacheKey != null)
        {
            lock (ReportDatesCacheLock)
            {
                if (
                    Cached13FReportDates.TryGetValue(cacheKey, out var entry)
                    && DateTime.UtcNow - entry.LoadedUtc < ReportDatesCacheTtl
                )
                    return new List<DateOnly>(entry.Dates);
            }
        }

        // The DISTINCT scan behind this list measures ~28s warm and can cross Npgsql's
        // default 30s command timeout cold — verified in production: the first
        // market-activity request after a container recreate (or a cache-TTL expiry)
        // 500s here before any aggregate even runs. Same headroom as the market-wide
        // aggregates, so a cold resolve becomes a slow success instead of a failure.
        ExtendCommandTimeoutForMarketWideAggregates();
        var dates = await Get13FAvailableReportDates().ToListAsync(cancellationToken);

        // An empty list is not cached: it only occurs before the first 13F import, and
        // caching it would blank the market-wide surfaces for a full TTL after data lands.
        if (cacheKey == null || dates.Count == 0)
            return dates;

        lock (ReportDatesCacheLock)
        {
            Cached13FReportDates[cacheKey] = (new List<DateOnly>(dates), DateTime.UtcNow);
        }
        return dates;
    }

    // Latest dates first — see GetAvailableReportDates for the ordering contract.
    public IQueryable<DateOnly> GetReportDatesByStock(CommonStock stock) =>
        GetHistoryByStock(stock).DistinctReportDatesDescending();

    // Latest 13F quarter-end dates first for one stock — see Get13FHistoryByStock for why
    // 13D/G event dates are excluded. The per-stock holders surfaces resolve their target and
    // comparison quarters off this list, so it must stay 13F-only (GH-4449).
    public IQueryable<DateOnly> Get13FReportDatesByStock(CommonStock stock) =>
        Get13FHistoryByStock(stock).DistinctReportDatesDescending();

    // Latest dates first — see GetAvailableReportDates for the ordering contract.
    public IQueryable<DateOnly> GetReportDatesByHolder(InstitutionalHolder holder) =>
        GetHistoryByHolder(holder).DistinctReportDatesDescending();

    // Latest 13F quarter-end dates first — see Get13FHistoryByHolder for why 13D/G
    // event dates are excluded.
    public IQueryable<DateOnly> Get13FReportDatesByHolder(InstitutionalHolder holder) =>
        Get13FHistoryByHolder(holder).DistinctReportDatesDescending();

    public IQueryable<InstitutionalHolding> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(h => h.AccessionNumber == accessionNumber);
    }

    // The market-wide two-quarter aggregates built on BothQuarters / GetCombinedQuarter
    // cost up to ~30s cold — the GROUP BY scans two quarters of holdings (~3.4M rows)
    // and the churn / combined forms add correlated NOT-EXISTS probes — which sits
    // exactly at Npgsql's default 30s command timeout, so a cache miss intermittently
    // dies mid-stream with "Timeout during reading attempt". Closed quarters read the
    // StockQuarterlyActivity snapshot instead, but the open filing window has no
    // materialised view yet, so its combined lane must run these live. Raising the
    // scope's timeout when such a query is composed gives that lane headroom; the
    // DbContext is scoped, so the raise lives and dies with the current request / tool
    // call, and an already-higher caller value (e.g. the snapshot rebuild worker's) is
    // kept.
    private static readonly TimeSpan MarketWideAggregateCommandTimeout = TimeSpan.FromSeconds(120);

    private void ExtendCommandTimeoutForMarketWideAggregates()
    {
        // Non-relational providers (the EF InMemory tests) have no command timeout.
        if (!DbContext.Database.IsRelational())
            return;
        var current = DbContext.Database.GetCommandTimeout();
        if (current == null || current < MarketWideAggregateCommandTimeout.TotalSeconds)
            DbContext.Database.SetCommandTimeout(MarketWideAggregateCommandTimeout);
    }

    // The two-quarter comparison window shared by every per-stock activity / churn /
    // double-down aggregate: both report dates are pulled in a single round trip and
    // each caller applies its own GROUP BY downstream. 13F-only: a Schedule 13D/G event
    // row whose date coincides with a quarter end would otherwise inflate the quarter's
    // totals and double-count the filer (GH-4449).
    private IQueryable<InstitutionalHolding> BothQuarters(DateOnly current, DateOnly previous)
    {
        ExtendCommandTimeoutForMarketWideAggregates();
        return GetAll()
            .Where(Is13F)
            .Where(h => h.ReportDate == current || h.ReportDate == previous);
    }

    // Per-stock aggregation of 13F activity across two quarters: totals, filer counts,
    // and the derived deltas drive the Top Buys / Top Sells leaderboards. New /
    // Sold-out filer-count metrics live in GetQuarterlyNewSoldOutPositions — they need
    // a set-difference between (stock, holder) pairs across the two quarters and don't
    // fit cleanly inside this single GROUP BY.
    // Both quarters are filtered in a single round trip; the conditional Sum / nested
    // Distinct().Count() forms translate server-side in EF Core.
    public IQueryable<MarketWideStockActivity> GetQuarterlyActivity(
        DateOnly current,
        DateOnly previous
    )
    {
        return BothQuarters(current, previous)
            .GroupBy(h => h.CommonStockId)
            .Select(g => new MarketWideStockActivity
            {
                CommonStockId = g.Key,
                CurrentShares = g.Sum(h => h.ReportDate == current ? h.Shares : 0L),
                PreviousShares = g.Sum(h => h.ReportDate == previous ? h.Shares : 0L),
                CurrentValue = g.Sum(h => h.ReportDate == current ? h.Value : 0L),
                PreviousValue = g.Sum(h => h.ReportDate == previous ? h.Value : 0L),
                CurrentFilerCount = g.Where(h => h.ReportDate == current)
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
                PreviousFilerCount = g.Where(h => h.ReportDate == previous)
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
            });
    }

    // Per-stock breadth across two 13F quarters: the same aggregates as
    // GetQuarterlyActivity but with sold-out stocks (CurrentFilerCount == 0)
    // filtered out, so the "most held" ranking is a list of currently-held
    // names. The caller decides the order (typically CurrentFilerCount desc);
    // the % of 13F universe is derived against
    // GetUniqueFilerCount(currentReportDate).
    public IQueryable<MarketWideStockActivity> GetMostHeld(DateOnly current, DateOnly previous) =>
        GetQuarterlyActivity(current, previous).Where(a => a.CurrentFilerCount > 0);

    // Total distinct 13F filers reporting on a given quarter — the denominator
    // for the "% of 13F universe" column on the most-held page. 13F-only: a filer whose
    // only row on the quarter end is a Schedule 13D/G event is not part of the 13F
    // universe and must not inflate the denominator (GH-4449).
    public IQueryable<Guid> GetUniqueFilerIds(DateOnly reportDate)
    {
        return GetAll()
            .Where(Is13F)
            .Where(h => h.ReportDate == reportDate)
            .Select(h => h.InstitutionalHolderId)
            .Distinct();
    }

    // Earliest 13F quarter each of the given holders appears on — the "is this the
    // filer's first 13F?" test behind the new-filer annotation on the buyers/sellers
    // table. A brand-new filer entity (often a CIK migration of an existing manager)
    // shows its whole book as a "buy"; flagging first-time filers lets the consumer
    // tell a genuinely new position from a filer-identity artifact.
    public IQueryable<KeyValuePair<Guid, DateOnly>> GetEarliest13FReportDates(
        IReadOnlyCollection<Guid> holderIds
    )
    {
        return GetAll()
            .Where(Is13F)
            .Where(h => holderIds.Contains(h.InstitutionalHolderId))
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new KeyValuePair<Guid, DateOnly>(g.Key, g.Min(h => h.ReportDate)));
    }

    // Per-stock churn between two 13F quarters: how many filers initiated a position
    // (in current, not in prior) and how many exited (in prior, not in current).
    // Implemented as two NOT-EXISTS subqueries against the same table — EF Core
    // translates the `!DbContext.Set<>().Any(...)` form to a SQL `NOT EXISTS` clause.
    public IQueryable<MarketWideStockChurn> GetQuarterlyNewSoldOutPositions(
        DateOnly current,
        DateOnly previous
    )
    {
        return BothQuarters(current, previous)
            .GroupBy(h => h.CommonStockId)
            .Select(g => new MarketWideStockChurn
            {
                CommonStockId = g.Key,
                NewFilerCount = g.Where(h =>
                        h.ReportDate == current
                        && !DbContext
                            .Set<InstitutionalHolding>()
                            .Any(p =>
                                p.ReportDate == previous
                                && p.FilingType == FilingType.Form13F
                                && p.CommonStockId == h.CommonStockId
                                && p.InstitutionalHolderId == h.InstitutionalHolderId
                            )
                    )
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
                SoldOutFilerCount = g.Where(h =>
                        h.ReportDate == previous
                        && !DbContext
                            .Set<InstitutionalHolding>()
                            .Any(c =>
                                c.ReportDate == current
                                && c.FilingType == FilingType.Form13F
                                && c.CommonStockId == h.CommonStockId
                                && c.InstitutionalHolderId == h.InstitutionalHolderId
                            )
                    )
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
            });
    }

    // Precomputed per-stock activity + churn for a quarter, maintained by
    // HoldingsAggregateRefreshService. The conviction heat map reads this instead
    // of deriving GetQuarterlyActivity + GetQuarterlyNewSoldOutPositions live.
    public IQueryable<StockQuarterlyActivity> GetStockActivitySnapshots(DateOnly reportDate) =>
        DbContext.Set<StockQuarterlyActivity>().Where(s => s.ReportDate == reportDate);

    // Double-down report: per-(holder, stock) positions where the share-count
    // increase from the prior quarter exceeds a given percentage threshold,
    // ranked by conviction (largest % increase first). Joins holder + stock
    // for display names so the caller doesn't need a second round trip.
    public IQueryable<DoubleDownPosition> GetDoubleDownPositions(
        DateOnly current,
        DateOnly previous,
        double minPctIncrease
    )
    {
        var aggregated = BothQuarters(current, previous)
            .GroupBy(h => new { h.InstitutionalHolderId, h.CommonStockId })
            .Select(g => new DoubleDownAggregate
            {
                InstitutionalHolderId = g.Key.InstitutionalHolderId,
                CommonStockId = g.Key.CommonStockId,
                CurrentShares = g.Sum(h => h.ReportDate == current ? h.Shares : 0L),
                PreviousShares = g.Sum(h => h.ReportDate == previous ? h.Shares : 0L),
                CurrentValue = g.Sum(h => h.ReportDate == current ? h.Value : 0L),
                PreviousValue = g.Sum(h => h.ReportDate == previous ? h.Value : 0L),
            });

        return ProjectDoubleDownPositions(aggregated, minPctIncrease);
    }

    // Recent filings feed: one row per 13F filing, read straight from the
    // InstitutionalFiling rollup (maintained at ingestion) instead of grouping the
    // whole holdings table on every request. Joins InstitutionalHolder for filer
    // metadata. The first-time-filer flag is deliberately NOT computed here: a per-row
    // correlated NOT EXISTS over the whole InstitutionalHolding table is evaluated far
    // beyond the page the caller keeps and times the request out (#3474). Callers order
    // by FilingDate descending, take their page, then call MarkNewFilers to set
    // IsNewFiler for just that page's filers.
    public IQueryable<RecentFiling> GetRecentFilings()
    {
        return DbContext
            .Set<InstitutionalFiling>()
            .Join(
                DbContext.Set<InstitutionalHolder>(),
                f => f.InstitutionalHolderId,
                h => h.Id,
                (f, h) =>
                    new RecentFiling
                    {
                        AccessionNumber = f.AccessionNumber,
                        InstitutionalHolderId = f.InstitutionalHolderId,
                        FilerName = h.Name,
                        FilerCik = h.Cik,
                        FilingDate = f.FilingDate,
                        ReportDate = f.ReportDate,
                        PositionCount = f.PositionCount,
                        TotalValue = f.TotalValue,
                        IsAmendment = f.IsAmendment,
                        ImportedAt = f.CreationTime,
                    }
            );
    }

    // Sets IsNewFiler on a materialised page of recent filings (from GetRecentFilings).
    // A filer is "new" when no holding was reported before the filing's report date —
    // equivalently, the filing's report date is at or before the filer's earliest holding
    // report date. Computed with a single grouped lookup bounded to the page's filers
    // (served by the (InstitutionalHolderId, ReportDate) index), replacing the per-row
    // correlated NOT EXISTS over the full holdings table that timed the page out (#3474).
    public async Task MarkNewFilers(
        IReadOnlyCollection<RecentFiling> filings,
        CancellationToken cancellationToken = default
    )
    {
        if (filings.Count == 0)
        {
            return;
        }

        var holderIds = filings.Select(f => f.InstitutionalHolderId).Distinct().ToList();

        var earliestReportDateByFiler = await DbContext
            .Set<InstitutionalHolding>()
            .Where(h => holderIds.Contains(h.InstitutionalHolderId))
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new { HolderId = g.Key, Earliest = g.Min(h => h.ReportDate) })
            .ToDictionaryAsync(x => x.HolderId, x => x.Earliest, cancellationToken);

        foreach (var filing in filings)
        {
            // A filer with no holdings on record (absent from the lookup) has nothing
            // earlier, so it is new; otherwise it is new only when this filing is at or
            // before its earliest reported holding.
            filing.IsNewFiler =
                !earliestReportDateByFiler.TryGetValue(
                    filing.InstitutionalHolderId,
                    out var earliest
                )
                || filing.ReportDate <= earliest;
        }
    }

    // Cross-sectional 13F screener. Aggregates per-stock filer-count / total-value /
    // new-position / sold-out-position metrics across the two snapshots in a single SQL
    // round trip, joins CommonStock + Industry for display columns and the % of float
    // calculation, and applies each non-null criterion as a server-side filter so result
    // pagination stays cheap. Caller materializes with ToListAsync.
    public IQueryable<ScreenerRow> Screen(
        ScreenerCriteria criteria,
        DateOnly current,
        DateOnly previous
    )
    {
        var aggregated = BothQuarters(current, previous)
            .GroupBy(h => h.CommonStockId)
            .Select(g => new
            {
                CommonStockId = g.Key,
                CurrentValue = g.Sum(h => h.ReportDate == current ? h.Value : 0L),
                PreviousValue = g.Sum(h => h.ReportDate == previous ? h.Value : 0L),
                CurrentShares = g.Sum(h => h.ReportDate == current ? h.Shares : 0L),
                CurrentFilerCount = g.Where(h => h.ReportDate == current)
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
                PreviousFilerCount = g.Where(h => h.ReportDate == previous)
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
                NewFilerCount = g.Where(h =>
                        h.ReportDate == current
                        && !DbContext
                            .Set<InstitutionalHolding>()
                            .Any(p =>
                                p.ReportDate == previous
                                && p.FilingType == FilingType.Form13F
                                && p.CommonStockId == h.CommonStockId
                                && p.InstitutionalHolderId == h.InstitutionalHolderId
                            )
                    )
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
                SoldOutFilerCount = g.Where(h =>
                        h.ReportDate == previous
                        && !DbContext
                            .Set<InstitutionalHolding>()
                            .Any(c =>
                                c.ReportDate == current
                                && c.FilingType == FilingType.Form13F
                                && c.CommonStockId == h.CommonStockId
                                && c.InstitutionalHolderId == h.InstitutionalHolderId
                            )
                    )
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
            });

        aggregated = aggregated
            .WhereIf(
                criteria.MinFilerCount.HasValue,
                r => r.CurrentFilerCount >= criteria.MinFilerCount.Value
            )
            .WhereIf(
                criteria.MaxFilerCount.HasValue,
                r => r.CurrentFilerCount <= criteria.MaxFilerCount.Value
            )
            .WhereIf(
                criteria.MinDeltaFilerCount.HasValue,
                r => r.CurrentFilerCount - r.PreviousFilerCount >= criteria.MinDeltaFilerCount.Value
            )
            .WhereIf(
                criteria.MaxDeltaFilerCount.HasValue,
                r => r.CurrentFilerCount - r.PreviousFilerCount <= criteria.MaxDeltaFilerCount.Value
            )
            .WhereIf(
                criteria.MinTotalValue.HasValue,
                r => r.CurrentValue >= criteria.MinTotalValue.Value
            )
            .WhereIf(
                criteria.MaxTotalValue.HasValue,
                r => r.CurrentValue <= criteria.MaxTotalValue.Value
            )
            .WhereIf(
                criteria.MinDeltaValue.HasValue,
                r => r.CurrentValue - r.PreviousValue >= criteria.MinDeltaValue.Value
            )
            .WhereIf(
                criteria.MaxDeltaValue.HasValue,
                r => r.CurrentValue - r.PreviousValue <= criteria.MaxDeltaValue.Value
            )
            .WhereIf(
                criteria.MinNewPositions.HasValue,
                r => r.NewFilerCount >= criteria.MinNewPositions.Value
            )
            .WhereIf(
                criteria.MinSoldOutPositions.HasValue,
                r => r.SoldOutFilerCount >= criteria.MinSoldOutPositions.Value
            );

        var joined = aggregated
            .Join(
                DbContext.Set<CommonStock>(),
                agg => agg.CommonStockId,
                cs => cs.Id,
                (agg, cs) => new { agg, cs }
            )
            .WhereIf(
                criteria.IndustryIds.Count > 0,
                j => j.cs.IndustryId != null && criteria.IndustryIds.Contains(j.cs.IndustryId.Value)
            )
            // SharesOutStanding == 0 means "unknown" in the current schema; any % filter
            // excludes those stocks rather than treating them as 100% held (false positive).
            .WhereIf(
                criteria.MinPctFloat.HasValue,
                j =>
                    j.cs.SharesOutStanding > 0
                    && (double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0
                        >= criteria.MinPctFloat.Value
            )
            .WhereIf(
                criteria.MaxPctFloat.HasValue,
                j =>
                    j.cs.SharesOutStanding > 0
                    && (double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0
                        <= criteria.MaxPctFloat.Value
            );

        return joined.Select(j => new ScreenerRow
        {
            CommonStockId = j.agg.CommonStockId,
            Ticker = j.cs.Ticker,
            Name = j.cs.Name,
            IndustryId = j.cs.IndustryId,
            IndustryName = j.cs.Industry != null ? j.cs.Industry.Name : null,
            SharesOutStanding = j.cs.SharesOutStanding,
            CurrentFilerCount = j.agg.CurrentFilerCount,
            PreviousFilerCount = j.agg.PreviousFilerCount,
            CurrentValue = j.agg.CurrentValue,
            PreviousValue = j.agg.PreviousValue,
            CurrentShares = j.agg.CurrentShares,
            NewFilerCount = j.agg.NewFilerCount,
            SoldOutFilerCount = j.agg.SoldOutFilerCount,
            PercentOfFloat =
                j.cs.SharesOutStanding > 0
                    ? (double?)((double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0)
                    : null,
        });
    }

    public IQueryable<FilingActivitySummary> GetFilingActivitySummary(
        CommonStock stock,
        DateOnly since
    )
    {
        return GetAll()
            .Where(h => h.CommonStockId == stock.Id && h.FilingDate >= since)
            .GroupBy(h => h.CommonStockId)
            .Select(g => new FilingActivitySummary
            {
                FilingCount = g.Select(h => h.AccessionNumber).Distinct().Count(),
                FilerCount = g.Select(h => h.InstitutionalHolderId).Distinct().Count(),
            });
    }

    public IQueryable<KeyValuePair<Guid, DateOnly>> GetFirstOwnedQuarters(
        CommonStock stock,
        IEnumerable<Guid> holderIds
    )
    {
        var ids = holderIds.ToList();
        // 13F-only: "first owned quarter" is the earliest 13F quarter a holder reported the
        // stock. A Schedule 13D/G stake carries a daily event date that would otherwise win
        // the Min and mislabel the first-owned quarter (GH-4449).
        return GetAll()
            .Where(h =>
                h.CommonStockId == stock.Id
                && ids.Contains(h.InstitutionalHolderId)
                && h.FilingType == FilingType.Form13F
            )
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new KeyValuePair<Guid, DateOnly>(g.Key, g.Min(h => h.ReportDate)));
    }

    // "Current combined" quarter: best-available holdings per holder. Uses current-
    // quarter data for holders who already filed, falls back to the previous quarter
    // for holders who haven't. The NOT EXISTS subquery identifies non-filers.
    // 13F-only on BOTH sides, mirroring GetCombinedQuarterByStock: a Schedule 13G/D
    // event row landing on the current quarter end must not count as "already filed" —
    // it would drop the holder's entire carried-forward 13F book from the combined view
    // and read as a mass liquidation while the filing window is open (GH-4449).
    public IQueryable<InstitutionalHolding> GetCombinedQuarter(DateOnly current, DateOnly previous)
    {
        return GetAll()
            .Where(Is13F)
            .Where(h =>
                h.ReportDate == current
                || (
                    h.ReportDate == previous
                    && !DbContext
                        .Set<InstitutionalHolding>()
                        .Any(c =>
                            c.ReportDate == current
                            && c.FilingType == FilingType.Form13F
                            && c.InstitutionalHolderId == h.InstitutionalHolderId
                        )
                )
            );
    }

    // Combined view scoped to one stock, 13F-only on BOTH sides (rows and the has-filed test —
    // unlike the market-wide GetCombinedQuarter): per-stock holder lists double-count a filer
    // whose 13D/G event date coincides with the quarter end, and that same coincidence must
    // not mark the fund as "filed" and drop its carried prior 13F row (GH-4449). The has-filed
    // test stays UNSCOPED by stock on purpose: a fund that filed this quarter without this
    // stock proved it sold out, so its prior row must not carry forward.
    public IQueryable<InstitutionalHolding> GetCombinedQuarterByStock(
        CommonStock stock,
        DateOnly current,
        DateOnly previous
    )
    {
        return GetAll()
            .Where(Is13F)
            .Where(h => h.CommonStockId == stock.Id)
            .Where(h =>
                h.ReportDate == current
                || (
                    h.ReportDate == previous
                    && !DbContext
                        .Set<InstitutionalHolding>()
                        .Any(c =>
                            c.ReportDate == current
                            && c.FilingType == FilingType.Form13F
                            && c.InstitutionalHolderId == h.InstitutionalHolderId
                        )
                )
            );
    }

    // Same combined per-stock view with the holder navigation eagerly loaded for rendering.
    public IQueryable<InstitutionalHolding> GetCombinedQuarterByStockWithHolder(
        CommonStock stock,
        DateOnly current,
        DateOnly previous
    )
    {
        return GetCombinedQuarterByStock(stock, current, previous)
            .Include(h => h.InstitutionalHolder);
    }

    // Distinct holder ids among the given set that filed ANY 13F at reportDate — the "has this
    // fund reported yet?" test behind the combined view's reported-so-far counts.
    public IQueryable<Guid> GetFiledHolderIdsAmong(
        DateOnly reportDate,
        IReadOnlyCollection<Guid> holderIds
    )
    {
        return GetAll()
            .Where(Is13F)
            .Where(h => h.ReportDate == reportDate && holderIds.Contains(h.InstitutionalHolderId))
            .Select(h => h.InstitutionalHolderId)
            .Distinct();
    }

    // Combined-quarter variant of GetQuarterlyActivity. The "current" side aggregates
    // the combined view (current filers + previous-quarter fallback for non-filers).
    // The "previous" side uses the actual previous quarter for delta comparison. For
    // non-filers the combined shares equal their previous shares, so the delta is zero.
    public IQueryable<MarketWideStockActivity> GetQuarterlyActivityCombined(
        DateOnly current,
        DateOnly previous
    )
    {
        return BothQuarters(current, previous)
            .GroupBy(h => h.CommonStockId)
            .Select(g => new MarketWideStockActivity
            {
                CommonStockId = g.Key,
                CurrentShares =
                    g.Where(h =>
                            h.ReportDate == current
                            || (
                                h.ReportDate == previous
                                && !DbContext
                                    .Set<InstitutionalHolding>()
                                    .Any(c =>
                                        c.ReportDate == current
                                        && c.FilingType == FilingType.Form13F
                                        && c.InstitutionalHolderId == h.InstitutionalHolderId
                                    )
                            )
                        )
                        .Sum(h => (long?)h.Shares)
                    ?? 0L,
                PreviousShares = g.Sum(h => h.ReportDate == previous ? h.Shares : 0L),
                CurrentValue =
                    g.Where(h =>
                            h.ReportDate == current
                            || (
                                h.ReportDate == previous
                                && !DbContext
                                    .Set<InstitutionalHolding>()
                                    .Any(c =>
                                        c.ReportDate == current
                                        && c.FilingType == FilingType.Form13F
                                        && c.InstitutionalHolderId == h.InstitutionalHolderId
                                    )
                            )
                        )
                        .Sum(h => (long?)h.Value)
                    ?? 0L,
                PreviousValue = g.Sum(h => h.ReportDate == previous ? h.Value : 0L),
                CurrentFilerCount = g.Where(h =>
                        h.ReportDate == current
                        || (
                            h.ReportDate == previous
                            && !DbContext
                                .Set<InstitutionalHolding>()
                                .Any(c =>
                                    c.ReportDate == current
                                    && c.FilingType == FilingType.Form13F
                                    && c.InstitutionalHolderId == h.InstitutionalHolderId
                                )
                        )
                    )
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
                PreviousFilerCount = g.Where(h => h.ReportDate == previous)
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
            });
    }

    public IQueryable<MarketWideStockActivity> GetMostHeldCombined(
        DateOnly current,
        DateOnly previous
    ) => GetQuarterlyActivityCombined(current, previous).Where(a => a.CurrentFilerCount > 0);

    public IQueryable<Guid> GetUniqueFilerIdsCombined(DateOnly current, DateOnly previous)
    {
        // Full-quarter DISTINCT over the combined view (incl. its NOT-EXISTS probe) —
        // the one market-wide aggregate not built on BothQuarters, so it opts into the
        // extended timeout itself.
        ExtendCommandTimeoutForMarketWideAggregates();
        return GetCombinedQuarter(current, previous)
            .Select(h => h.InstitutionalHolderId)
            .Distinct();
    }

    // Combined-quarter variant of churn detection. "New" = holder appears in the
    // combined view but not in the previous quarter. "Sold-out" = holder appears in
    // the previous quarter but not in the combined view.
    public IQueryable<MarketWideStockChurn> GetQuarterlyNewSoldOutPositionsCombined(
        DateOnly current,
        DateOnly previous
    )
    {
        return BothQuarters(current, previous)
            .GroupBy(h => h.CommonStockId)
            .Select(g => new MarketWideStockChurn
            {
                CommonStockId = g.Key,
                // New: in current quarter and not in previous (only actual current-quarter filers
                // can introduce genuinely new positions).
                NewFilerCount = g.Where(h =>
                        h.ReportDate == current
                        && !DbContext
                            .Set<InstitutionalHolding>()
                            .Any(p =>
                                p.ReportDate == previous
                                && p.FilingType == FilingType.Form13F
                                && p.CommonStockId == h.CommonStockId
                                && p.InstitutionalHolderId == h.InstitutionalHolderId
                            )
                    )
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
                // Sold-out: in previous and not in the combined view. A holder counts as
                // sold-out only if they filed the current quarter (proving they dropped
                // the position). Non-filers are assumed to still hold.
                SoldOutFilerCount = g.Where(h =>
                        h.ReportDate == previous
                        && DbContext
                            .Set<InstitutionalHolding>()
                            .Any(c =>
                                c.ReportDate == current
                                && c.FilingType == FilingType.Form13F
                                && c.InstitutionalHolderId == h.InstitutionalHolderId
                            )
                        && !DbContext
                            .Set<InstitutionalHolding>()
                            .Any(c =>
                                c.ReportDate == current
                                && c.FilingType == FilingType.Form13F
                                && c.CommonStockId == h.CommonStockId
                                && c.InstitutionalHolderId == h.InstitutionalHolderId
                            )
                    )
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
            });
    }

    public IQueryable<DoubleDownPosition> GetDoubleDownPositionsCombined(
        DateOnly current,
        DateOnly previous,
        double minPctIncrease
    )
    {
        var aggregated = BothQuarters(current, previous)
            .GroupBy(h => new { h.InstitutionalHolderId, h.CommonStockId })
            .Select(g => new DoubleDownAggregate
            {
                InstitutionalHolderId = g.Key.InstitutionalHolderId,
                CommonStockId = g.Key.CommonStockId,
                CurrentShares =
                    g.Where(h =>
                            h.ReportDate == current
                            || (
                                h.ReportDate == previous
                                && !DbContext
                                    .Set<InstitutionalHolding>()
                                    .Any(c =>
                                        c.ReportDate == current
                                        && c.FilingType == FilingType.Form13F
                                        && c.InstitutionalHolderId == h.InstitutionalHolderId
                                    )
                            )
                        )
                        .Sum(h => (long?)h.Shares)
                    ?? 0L,
                PreviousShares = g.Sum(h => h.ReportDate == previous ? h.Shares : 0L),
                CurrentValue =
                    g.Where(h =>
                            h.ReportDate == current
                            || (
                                h.ReportDate == previous
                                && !DbContext
                                    .Set<InstitutionalHolding>()
                                    .Any(c =>
                                        c.ReportDate == current
                                        && c.FilingType == FilingType.Form13F
                                        && c.InstitutionalHolderId == h.InstitutionalHolderId
                                    )
                            )
                        )
                        .Sum(h => (long?)h.Value)
                    ?? 0L,
                PreviousValue = g.Sum(h => h.ReportDate == previous ? h.Value : 0L),
            });

        return ProjectDoubleDownPositions(aggregated, minPctIncrease);
    }

    // A double down needs a non-trivial existing position: a 1-share / near-$0 prior
    // base turns an ordinary new position into a "+190,898,100%" share-change artifact
    // that dominates the percentage ranking and buries genuine conviction increases.
    public const long MinDoubleDownPreviousValue = 10_000;

    // Shared tail for both double-down queries: applies the common increase
    // filters to the per-filer aggregate, then joins filer + stock metadata.
    // Identical generated SQL whether the aggregate came from the single-quarter
    // or combined-quarter projection.
    private IQueryable<DoubleDownPosition> ProjectDoubleDownPositions(
        IQueryable<DoubleDownAggregate> aggregated,
        double minPctIncrease
    ) =>
        aggregated
            .Where(a =>
                a.PreviousShares > 0
                && a.PreviousValue >= MinDoubleDownPreviousValue
                && a.CurrentShares > a.PreviousShares
            )
            .Where(a =>
                (double)(a.CurrentShares - a.PreviousShares) / a.PreviousShares * 100.0
                >= minPctIncrease
            )
            .Join(
                DbContext.Set<InstitutionalHolder>(),
                a => a.InstitutionalHolderId,
                h => h.Id,
                (a, h) =>
                    new
                    {
                        a,
                        FilerName = h.Name,
                        FilerCik = h.Cik,
                    }
            )
            .Join(
                DbContext.Set<CommonStock>(),
                x => x.a.CommonStockId,
                s => s.Id,
                (x, s) =>
                    new DoubleDownPosition
                    {
                        InstitutionalHolderId = x.a.InstitutionalHolderId,
                        FilerName = x.FilerName,
                        FilerCik = x.FilerCik,
                        CommonStockId = x.a.CommonStockId,
                        Ticker = s.Ticker,
                        StockName = s.Name,
                        CurrentShares = x.a.CurrentShares,
                        PreviousShares = x.a.PreviousShares,
                        CurrentValue = x.a.CurrentValue,
                        PreviousValue = x.a.PreviousValue,
                    }
            );

    public IQueryable<MarketWideStockActivity> GetQuarterlyActivity(
        DateOnly current,
        DateOnly previous,
        bool combined
    ) =>
        combined
            ? GetQuarterlyActivityCombined(current, previous)
            : GetQuarterlyActivity(current, previous);

    public IQueryable<MarketWideStockActivity> GetMostHeld(
        DateOnly current,
        DateOnly previous,
        bool combined
    ) => combined ? GetMostHeldCombined(current, previous) : GetMostHeld(current, previous);

    public IQueryable<Guid> GetUniqueFilerIds(DateOnly current, DateOnly previous, bool combined) =>
        combined ? GetUniqueFilerIdsCombined(current, previous) : GetUniqueFilerIds(current);

    public IQueryable<MarketWideStockChurn> GetQuarterlyNewSoldOutPositions(
        DateOnly current,
        DateOnly previous,
        bool combined
    ) =>
        combined
            ? GetQuarterlyNewSoldOutPositionsCombined(current, previous)
            : GetQuarterlyNewSoldOutPositions(current, previous);

    public IQueryable<DoubleDownPosition> GetDoubleDownPositions(
        DateOnly current,
        DateOnly previous,
        double minPctIncrease,
        bool combined
    ) =>
        combined
            ? GetDoubleDownPositionsCombined(current, previous, minPctIncrease)
            : GetDoubleDownPositions(current, previous, minPctIncrease);

    // Intermediate per-filer aggregate shared by both double-down projections.
    // Never materialized — only its members feed the EF Core SQL translation —
    // so a member-init named type is equivalent to the former anonymous type.
    private sealed class DoubleDownAggregate
    {
        public Guid InstitutionalHolderId { get; set; }
        public Guid CommonStockId { get; set; }
        public long CurrentShares { get; set; }
        public long PreviousShares { get; set; }
        public long CurrentValue { get; set; }
        public long PreviousValue { get; set; }
    }
}

file static class InstitutionalHoldingQueryableExtensions
{
    public static IQueryable<DateOnly> DistinctReportDatesDescending(
        this IQueryable<InstitutionalHolding> source
    ) => source.Select(h => h.ReportDate).Distinct().OrderByDescending(d => d);
}
