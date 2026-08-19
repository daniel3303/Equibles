using System.Linq.Expressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
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

    // Lightweight two-quarter holder/listing projection for the per-stock movers surface. The prior
    // implementation materialised every holding entity and holder navigation from both quarters
    // before grouping in memory; popular stocks turned that into thousands of wide rows per call.
    public IQueryable<HolderStockActivity> Get13FHolderActivityByStock(
        CommonStock stock,
        DateOnly currentReportDate,
        DateOnly? previousReportDate
    )
    {
        return GetAll()
            .Where(Is13F)
            .Where(h => h.CommonStockId == stock.Id)
            .Where(h =>
                h.ReportDate == currentReportDate
                || (previousReportDate.HasValue && h.ReportDate == previousReportDate.Value)
            )
            .GroupBy(h => new { h.InstitutionalHolderId, h.ListedTicker })
            .Select(g => new HolderStockActivity
            {
                InstitutionalHolderId = g.Key.InstitutionalHolderId,
                ListedTicker = g.Key.ListedTicker,
                CurrentShares = g.Sum(h => h.ReportDate == currentReportDate ? h.Shares : 0L),
                PreviousShares = g.Sum(h =>
                    previousReportDate.HasValue && h.ReportDate == previousReportDate.Value
                        ? h.Shares
                        : 0L
                ),
                CurrentValue = g.Sum(h => h.ReportDate == currentReportDate ? h.Value : 0L),
                PreviousValue = g.Sum(h =>
                    previousReportDate.HasValue && h.ReportDate == previousReportDate.Value
                        ? h.Value
                        : 0L
                ),
                CurrentPositionCount = g.Count(h => h.ReportDate == currentReportDate),
                PreviousPositionCount = g.Count(h =>
                    previousReportDate.HasValue && h.ReportDate == previousReportDate.Value
                ),
            });
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

    // Minimal per-stock projection for portfolio-summary maths. Loading thousands of full
    // tracked holding entities for two quarters made a cold summary proportional to the raw
    // filing row count even though concentration and turnover only use these three fields.
    public IQueryable<InstitutionPortfolioPosition> Get13FPositionAggregatesByHolder(
        InstitutionalHolder holder,
        DateOnly reportDate
    )
    {
        return Get13FByHolder(holder, reportDate)
            .GroupBy(h => h.CommonStockId)
            .Select(g => new InstitutionPortfolioPosition
            {
                CommonStockId = g.Key,
                Shares = g.Sum(h => h.Shares),
                Value = g.Sum(h => h.Value),
            });
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

    // The live DISTINCT behind Get13FAvailableReportDates scans the whole holdings index
    // (~34M rows) to produce fewer than a hundred quarter ends and measures ~28s warm. The
    // worker-maintained AumQuarterlySnapshot table is the bounded one-row-per-quarter date spine
    // for request paths; the process cache below is retained only for a fresh/unbackfilled
    // database whose snapshot table is still empty.
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
        var latestLiveDate = await GetAll()
            .Where(Is13F)
            .OrderByDescending(h => h.ReportDate)
            .Select(h => (DateOnly?)h.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestLiveDate is not { } latest)
            return [];

        var snapshotDates = await DbContext
            .Set<AumQuarterlySnapshot>()
            .Where(s => s.ReportDate <= latest)
            .Select(s => s.ReportDate)
            .OrderByDescending(date => date)
            .ToListAsync(cancellationToken);
        if (snapshotDates.Count > 0)
        {
            // Aggregate refresh follows ingestion. Keep the just-opened quarter selectable
            // during that short lag without falling back to the corpus-wide DISTINCT.
            if (latest > snapshotDates[0])
                snapshotDates.Insert(0, latest);
            return snapshotDates;
        }

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

        // Fresh databases have no aggregate spine yet. Preserve the exact live fallback with
        // extra timeout headroom until the first snapshot rebuild completes.
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

    // Snapshot-backed twin for request paths. The live DISTINCT above scans every historical
    // position for a heavily held stock; under ingest pressure that scan exhausted the 30-second
    // command timeout on NVDA, MU and MPWR. StockQuarterlyActivity already materialises exactly
    // one 13F row per (stock, quarter), so rows with current holders are the authoritative cheap
    // date spine once present. Sold-out rows remain in the snapshot to describe the exit but cannot
    // be offered as a holdings quarter because they intentionally have no current positions.
    //
    // The newest live holding is checked separately because aggregate refresh follows ingestion:
    // during that short lag, prepend the new quarter instead of serving a stale selector. A stock
    // with no snapshot rows falls back to the live query so a newly listed company never loses its
    // prior quarters while the aggregate backfill catches up.
    public async Task<List<DateOnly>> Get13FReportDatesByStockSnapshotBacked(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        var latestLiveDate = await Get13FHistoryByStock(stock)
            .OrderByDescending(h => h.ReportDate)
            .Select(h => (DateOnly?)h.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestLiveDate is not { } latest)
        {
            return [];
        }

        var dates = await DbContext
            .Set<StockQuarterlyActivity>()
            .Where(s =>
                s.CommonStockId == stock.Id && s.CurrentFilerCount > 0 && s.ReportDate <= latest
            )
            .OrderByDescending(s => s.ReportDate)
            .Select(s => s.ReportDate)
            .ToListAsync(cancellationToken);

        if (dates.Count == 0)
        {
            return await Get13FReportDatesByStock(stock).ToListAsync(cancellationToken);
        }

        if (latest > dates[0])
        {
            dates.Insert(0, latest);
        }

        return dates;
    }

    // Latest dates first — see GetAvailableReportDates for the ordering contract.
    public IQueryable<DateOnly> GetReportDatesByHolder(InstitutionalHolder holder) =>
        GetHistoryByHolder(holder).DistinctReportDatesDescending();

    // Latest 13F quarter-end dates first — see Get13FHistoryByHolder for why 13D/G
    // event dates are excluded.
    public IQueryable<DateOnly> Get13FReportDatesByHolder(InstitutionalHolder holder) =>
        Get13FHistoryByHolder(holder).DistinctReportDatesDescending();

    // Request-path twin of Get13FReportDatesByHolder. A large filer can carry thousands of
    // positions per quarter, so DISTINCT over its full history turns a one-row-per-quarter
    // lookup into a multi-second cold scan. HolderQuarterlySnapshot is maintained in the same
    // dirty-quarter transaction as the market aggregates. The newest live row bounds stale
    // snapshots and is prepended while the refresh worker trails ingestion.
    public async Task<List<DateOnly>> Get13FReportDatesByHolderSnapshotBacked(
        InstitutionalHolder holder,
        CancellationToken cancellationToken = default
    )
    {
        var latestLiveDate = await Get13FHistoryByHolder(holder)
            .OrderByDescending(h => h.ReportDate)
            .Select(h => (DateOnly?)h.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestLiveDate is not { } latest)
            return [];

        var dates = await DbContext
            .Set<HolderQuarterlySnapshot>()
            .Where(s => s.InstitutionalHolderId == holder.Id && s.ReportDate <= latest)
            .OrderByDescending(s => s.ReportDate)
            .Select(s => s.ReportDate)
            .ToListAsync(cancellationToken);
        if (dates.Count == 0)
            return await Get13FReportDatesByHolder(holder).ToListAsync(cancellationToken);

        if (latest > dates[0])
            dates.Insert(0, latest);
        return dates;
    }

    public IQueryable<HolderQuarterlySnapshot> GetHolderQuarterlySnapshots(
        InstitutionalHolder holder
    ) => DbContext.Set<HolderQuarterlySnapshot>().Where(s => s.InstitutionalHolderId == holder.Id);

    // Snapshot-first holder trend source. A holder with no materialized rows can exist on a
    // fresh database or during the first aggregate refresh, so only that missing holder falls
    // back to a holder-scoped GROUP BY. Existing snapshot slices remain authoritative while
    // dirty and never trigger the expensive live path.
    public async Task<List<HolderQuarterlySnapshot>> GetHolderQuarterlySnapshotsSnapshotBacked(
        IReadOnlyCollection<Guid> holderIds,
        CancellationToken cancellationToken = default
    )
    {
        if (holderIds.Count == 0)
            return [];

        var snapshots = await DbContext
            .Set<HolderQuarterlySnapshot>()
            .AsNoTracking()
            .Where(s => holderIds.Contains(s.InstitutionalHolderId))
            .ToListAsync(cancellationToken);
        var coveredHolderIds = snapshots.Select(s => s.InstitutionalHolderId).ToHashSet();
        var missingHolderIds = holderIds.Where(id => !coveredHolderIds.Contains(id)).ToList();
        if (missingHolderIds.Count == 0)
            return snapshots;

        var live = await GetAll()
            .Where(Is13F)
            .Where(h => missingHolderIds.Contains(h.InstitutionalHolderId))
            .GroupBy(h => new { h.InstitutionalHolderId, h.ReportDate })
            .Select(g => new
            {
                g.Key.InstitutionalHolderId,
                g.Key.ReportDate,
                FilingDate = g.Max(h => h.FilingDate),
                Aum = g.Sum(h => h.Value),
                PositionCount = g.Count(),
                StockCount = g.Select(h => h.CommonStockId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);
        snapshots.AddRange(
            live.Select(row => new HolderQuarterlySnapshot
            {
                InstitutionalHolderId = row.InstitutionalHolderId,
                ReportDate = row.ReportDate,
                FilingDate = row.FilingDate,
                Aum = row.Aum,
                PositionCount = row.PositionCount,
                StockCount = row.StockCount,
            })
        );
        return snapshots;
    }

    public Task<List<HolderQuarterlySnapshot>> GetHolderQuarterlySnapshotsSnapshotBacked(
        InstitutionalHolder holder,
        CancellationToken cancellationToken = default
    ) => GetHolderQuarterlySnapshotsSnapshotBacked([holder.Id], cancellationToken);

    // Version stamp for process-local market-aggregate caches. DirtyAt is set in the import
    // transaction and cleared only after every quarter snapshot has rebuilt, so callers bypass
    // the process cache during the ingest-to-refresh gap while still serving the bounded existing
    // snapshot. ComputedAt changes on every completed rebuild and invalidates the fixed cache key
    // without an inter-process signal.
    public async Task<HoldingsSnapshotState> GetMarketActivitySnapshotState(
        DateOnly reportDate,
        DateOnly previousReportDate,
        bool combined,
        CancellationToken cancellationToken = default
    )
    {
        var dates = new[] { reportDate, previousReportDate };
        var dirty = await DbContext
            .Set<AumQuarterlySnapshot>()
            .AnyAsync(s => dates.Contains(s.ReportDate) && s.DirtyAt != null, cancellationToken);
        var computedAt = combined
            ? await DbContext
                .Set<StockQuarterlyActivityCombined>()
                .Where(s => s.ReportDate == reportDate)
                .Select(s => (DateTime?)s.ComputedAt)
                .MaxAsync(cancellationToken)
            : await DbContext
                .Set<StockQuarterlyActivity>()
                .Where(s => s.ReportDate == reportDate)
                .Select(s => (DateTime?)s.ComputedAt)
                .MaxAsync(cancellationToken);
        return new HoldingsSnapshotState(dirty, computedAt);
    }

    // Per-holder counterpart used by InstitutionPortfolioSummaryProvider. Both quarter stamps
    // participate because turnover reads current and prior positions; a rebuild of either side
    // must produce a new cache identity.
    public async Task<HolderSummarySnapshotState> GetHolderSummarySnapshotState(
        Guid holderId,
        DateOnly currentReportDate,
        DateOnly? previousReportDate,
        CancellationToken cancellationToken = default
    )
    {
        var dates = previousReportDate is { } previous
            ? new[] { currentReportDate, previous }
            : new[] { currentReportDate };
        var dirty = await DbContext
            .Set<AumQuarterlySnapshot>()
            .AnyAsync(s => dates.Contains(s.ReportDate) && s.DirtyAt != null, cancellationToken);
        var versions = await DbContext
            .Set<HolderQuarterlySnapshot>()
            .Where(s => s.InstitutionalHolderId == holderId && dates.Contains(s.ReportDate))
            .Select(s => new { s.ReportDate, s.ComputedAt })
            .ToListAsync(cancellationToken);
        return new HolderSummarySnapshotState(
            dirty,
            versions
                .Where(s => s.ReportDate == currentReportDate)
                .Select(s => (DateTime?)s.ComputedAt)
                .SingleOrDefault(),
            previousReportDate is { } prior
                ? versions
                    .Where(s => s.ReportDate == prior)
                    .Select(s => (DateTime?)s.ComputedAt)
                    .SingleOrDefault()
                : null
        );
    }

    public IQueryable<InstitutionalHolding> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(h => h.AccessionNumber == accessionNumber);
    }

    // The market-wide two-quarter aggregates built on BothQuarters / GetCombinedQuarter
    // cost up to ~30s cold — the GROUP BY scans two quarters of holdings (~3.4M rows)
    // and the churn / combined forms add correlated NOT-EXISTS probes — which sits
    // exactly at Npgsql's default 30s command timeout, so a cache miss intermittently
    // dies mid-stream with "Timeout during reading attempt". Closed quarters read the
    // StockQuarterlyActivity snapshots instead. These live queries remain only as the
    // fresh-database fallback while the matching closed or combined snapshot is empty.
    // Raising the scope's timeout when such a fallback is composed gives it headroom;
    // the DbContext is scoped, so the raise lives and dies with the current request, and
    // an already-higher caller value (e.g. the snapshot rebuild worker's) is kept.
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

    // Snapshot-first denominator for public most-held rankings. Closed-quarter AUM snapshots
    // already materialise the exact 13F filer universe, avoiding a corpus-wide DISTINCT that
    // measured tens of seconds and could hit the command timeout. A positive dirty snapshot is
    // served uncached until its rebuild lands; only a missing/zero stub falls back to the exact
    // count. During an open filing window, the matching combined activity snapshot proves that
    // the bounded holder-quarter slices are available; their distinct union is the exact
    // carry-forward universe. Only a genuinely empty snapshot falls back to the live corpus.
    public async Task<int> Get13FUniverseFilerCount(
        DateOnly current,
        DateOnly previous,
        bool combined,
        CancellationToken cancellationToken = default
    )
    {
        if (combined)
        {
            var hasCombinedSnapshot = await DbContext
                .Set<StockQuarterlyActivityCombined>()
                .AsNoTracking()
                .AnyAsync(s => s.ReportDate == current, cancellationToken);
            if (hasCombinedSnapshot)
            {
                return await DbContext
                    .Set<HolderQuarterlySnapshot>()
                    .AsNoTracking()
                    .Where(s => s.ReportDate == current || s.ReportDate == previous)
                    .Select(s => s.InstitutionalHolderId)
                    .Distinct()
                    .CountAsync(cancellationToken);
            }
        }
        else
        {
            var snapshot = await DbContext
                .Set<AumQuarterlySnapshot>()
                .AsNoTracking()
                .Where(s => s.ReportDate == current)
                .Select(s => (int?)s.FilerCount)
                .SingleOrDefaultAsync(cancellationToken);
            if (snapshot is > 0)
                return snapshot.Value;
        }

        return await GetUniqueFilerIds(current, previous, combined).CountAsync(cancellationToken);
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

    public IQueryable<StockQuarterlyActivity> GetStockActivitySnapshotsByStock(CommonStock stock) =>
        DbContext.Set<StockQuarterlyActivity>().Where(s => s.CommonStockId == stock.Id);

    // Snapshot-first stock trend source. The newest live row bounds stale snapshot history and
    // is appended while refresh lags; the stock-scoped live GROUP BY remains bootstrap-only.
    public async Task<List<StockQuarterlyActivity>> GetStockActivitySnapshotsByStockSnapshotBacked(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        var latestLiveDate = await Get13FHistoryByStock(stock)
            .OrderByDescending(h => h.ReportDate)
            .Select(h => (DateOnly?)h.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestLiveDate is not { } latest)
        {
            return [];
        }

        var snapshots = await GetStockActivitySnapshotsByStock(stock)
            .Where(s => s.ReportDate <= latest)
            .AsNoTracking()
            .OrderBy(s => s.ReportDate)
            .ToListAsync(cancellationToken);
        if (snapshots.Count == 0)
            return await GetLiveStockActivity(stock, cancellationToken);

        var snapshotDates = snapshots.Select(row => row.ReportDate).ToArray();
        var listingRows = await DbContext
            .Set<StockQuarterlyListingActivity>()
            .AsNoTracking()
            .Where(row =>
                row.CommonStockId == stock.Id
                && !row.IsCombined
                && snapshotDates.Contains(row.ReportDate)
            )
            .ToListAsync(cancellationToken);
        var listingByDate = listingRows
            .GroupBy(row => row.ReportDate)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StockQuarterlyListingActivity>)group.ToList()
            );
        if (
            snapshots.Any(row =>
                !listingByDate.TryGetValue(row.ReportDate, out var listings)
                || listings.Any(listing => listing.ComputedAt != row.ComputedAt)
            )
        )
            return await GetLiveStockActivity(stock, cancellationToken);

        foreach (var snapshot in snapshots)
            snapshot.ListingShares = listingByDate[snapshot.ReportDate];
        if (latest > snapshots[^1].ReportDate)
        {
            snapshots.Add(await GetLiveStockActivityPoint(stock, latest, cancellationToken));
        }
        return snapshots;
    }

    // Snapshot-first open-window point for one stock. Ownership-history surfaces replace only
    // their newest as-filed row with this combined view, so they never loop over raw holdings by
    // quarter. The exact-listing rows are version-checked with the stock row before split
    // restatement; a missing generation falls back only to this stock's two-quarter aggregate.
    public async Task<StockQuarterlyActivity> GetCombinedStockActivitySnapshotBacked(
        CommonStock stock,
        DateOnly currentReportDate,
        DateOnly previousReportDate,
        CancellationToken cancellationToken = default
    )
    {
        var snapshot = await GetStockActivitySnapshotsCombined(currentReportDate)
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommonStockId == stock.Id, cancellationToken);
        if (snapshot != null)
        {
            var listingShares = await GetStockListingActivitySnapshots(
                    currentReportDate,
                    combined: true
                )
                .AsNoTracking()
                .Where(row => row.CommonStockId == stock.Id)
                .ToListAsync(cancellationToken);
            if (
                listingShares.Count > 0
                && listingShares.All(row => row.ComputedAt == snapshot.ComputedAt)
            )
            {
                return ToStockActivity(snapshot, listingShares);
            }
        }

        var current = await Get13FByStock(stock, currentReportDate)
            .AsNoTracking()
            .Select(row => new { row.InstitutionalHolderId, row.Value })
            .ToListAsync(cancellationToken);
        var previous = await Get13FByStock(stock, previousReportDate)
            .AsNoTracking()
            .Select(row => new { row.InstitutionalHolderId, row.Value })
            .ToListAsync(cancellationToken);
        if (current.Count == 0 && previous.Count == 0)
            return null;

        var currentHolders = current.Select(row => row.InstitutionalHolderId).ToHashSet();
        var previousHolders = previous.Select(row => row.InstitutionalHolderId).ToHashSet();
        var filedPreviousHolders =
            previousHolders.Count == 0
                ? new HashSet<Guid>()
                : (
                    await GetFiledHolderIdsAmong(currentReportDate, previousHolders)
                        .ToListAsync(cancellationToken)
                ).ToHashSet();
        var carried = previous
            .Where(row => !filedPreviousHolders.Contains(row.InstitutionalHolderId))
            .ToList();

        var liveListingShares = await GetLiveListingShareActivity(
            currentReportDate,
            previousReportDate,
            combined: true,
            [stock.Id],
            cancellationToken
        );
        if (liveListingShares.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot load combined holdings activity for {stock.Ticker} without listing-share rows."
            );
        }

        return new StockQuarterlyActivity
        {
            CommonStockId = stock.Id,
            ReportDate = currentReportDate,
            PreviousReportDate = previousReportDate,
            CurrentShares = liveListingShares.Sum(row => row.CurrentShares),
            PreviousShares = liveListingShares.Sum(row => row.PreviousShares),
            CurrentValue = current.Sum(row => row.Value) + carried.Sum(row => row.Value),
            PreviousValue = previous.Sum(row => row.Value),
            CurrentFilerCount = currentHolders
                .Union(carried.Select(row => row.InstitutionalHolderId))
                .Count(),
            PreviousFilerCount = previousHolders.Count,
            NewFilerCount = currentHolders.Count(id => !previousHolders.Contains(id)),
            SoldOutFilerCount = filedPreviousHolders.Count(id => !currentHolders.Contains(id)),
            ListingShares = liveListingShares,
        };
    }

    private static StockQuarterlyActivity ToStockActivity(
        StockQuarterlyActivityCombined snapshot,
        IReadOnlyList<StockQuarterlyListingActivity> listingShares
    ) =>
        new()
        {
            CommonStockId = snapshot.CommonStockId,
            ReportDate = snapshot.ReportDate,
            PreviousReportDate = snapshot.PreviousReportDate,
            CurrentShares = snapshot.CurrentShares,
            PreviousShares = snapshot.PreviousShares,
            CurrentValue = snapshot.CurrentValue,
            PreviousValue = snapshot.PreviousValue,
            CurrentFilerCount = snapshot.CurrentFilerCount,
            PreviousFilerCount = snapshot.PreviousFilerCount,
            NewFilerCount = snapshot.NewFilerCount,
            SoldOutFilerCount = snapshot.SoldOutFilerCount,
            ComputedAt = snapshot.ComputedAt,
            ListingShares = listingShares,
        };

    // Refresh-lag fallback for one just-opened quarter. Its comparison is bounded to that
    // quarter and the immediately preceding live quarter instead of regrouping the stock's
    // entire filing history whenever the aggregate worker trails ingestion.
    private async Task<StockQuarterlyActivity> GetLiveStockActivityPoint(
        CommonStock stock,
        DateOnly currentReportDate,
        CancellationToken cancellationToken
    )
    {
        var previousReportDate = await Get13FHistoryByStock(stock)
            .Where(holding => holding.ReportDate < currentReportDate)
            .OrderByDescending(holding => holding.ReportDate)
            .Select(holding => (DateOnly?)holding.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);
        var activity = await Get13FHolderActivityByStock(
                stock,
                currentReportDate,
                previousReportDate
            )
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var currentHolders = activity
            .Where(row => row.CurrentPositionCount > 0)
            .Select(row => row.InstitutionalHolderId)
            .ToHashSet();
        var previousHolders = activity
            .Where(row => row.PreviousPositionCount > 0)
            .Select(row => row.InstitutionalHolderId)
            .ToHashSet();
        var listingShares = activity
            .GroupBy(row => row.ListedTicker ?? stock.Ticker)
            .Select(group => new StockQuarterlyListingActivity
            {
                CommonStockId = stock.Id,
                ReportDate = currentReportDate,
                IsCombined = false,
                PriceSeriesTicker = group.Key,
                CurrentShares = group.Sum(row => row.CurrentShares),
                PreviousShares = group.Sum(row => row.PreviousShares),
            })
            .ToList();
        return new StockQuarterlyActivity
        {
            CommonStockId = stock.Id,
            ReportDate = currentReportDate,
            PreviousReportDate = previousReportDate,
            CurrentShares = activity.Sum(row => row.CurrentShares),
            PreviousShares = activity.Sum(row => row.PreviousShares),
            CurrentValue = activity.Sum(row => row.CurrentValue),
            PreviousValue = activity.Sum(row => row.PreviousValue),
            CurrentFilerCount = currentHolders.Count,
            PreviousFilerCount = previousHolders.Count,
            NewFilerCount = currentHolders.Except(previousHolders).Count(),
            SoldOutFilerCount = previousHolders.Except(currentHolders).Count(),
            ListingShares = listingShares,
        };
    }

    private async Task<List<StockQuarterlyActivity>> GetLiveStockActivity(
        CommonStock stock,
        CancellationToken cancellationToken
    )
    {
        var aggregates = await Get13FHistoryByStock(stock)
            .GroupBy(holding => holding.ReportDate)
            .Select(group => new
            {
                ReportDate = group.Key,
                Shares = group.Sum(holding => holding.Shares),
                Value = group.Sum(holding => holding.Value),
                FilerCount = group
                    .Select(holding => holding.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
            })
            .OrderBy(row => row.ReportDate)
            .ToListAsync(cancellationToken);
        var exact = await Get13FHistoryByStock(stock)
            .GroupBy(holding => new { holding.ReportDate, holding.ListedTicker })
            .Select(group => new
            {
                group.Key.ReportDate,
                PriceSeriesTicker = group.Key.ListedTicker ?? stock.Ticker,
                Shares = group.Sum(holding => holding.Shares),
            })
            .ToListAsync(cancellationToken);
        var exactByDate = exact
            .GroupBy(row => row.ReportDate)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(row => row.PriceSeriesTicker, row => row.Shares)
            );

        var result = new List<StockQuarterlyActivity>(aggregates.Count);
        for (var index = 0; index < aggregates.Count; index++)
        {
            var current = aggregates[index];
            var previous = index > 0 ? aggregates[index - 1] : null;
            var currentSeries = exactByDate[current.ReportDate];
            var previousSeries = previous is null
                ? new Dictionary<string, long>()
                : exactByDate[previous.ReportDate];
            var listingShares = currentSeries
                .Keys.Union(previousSeries.Keys)
                .Select(ticker => new StockQuarterlyListingActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = current.ReportDate,
                    IsCombined = false,
                    PriceSeriesTicker = ticker,
                    CurrentShares = currentSeries.GetValueOrDefault(ticker),
                    PreviousShares = previousSeries.GetValueOrDefault(ticker),
                })
                .ToList();
            result.Add(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = current.ReportDate,
                    PreviousReportDate = previous?.ReportDate,
                    CurrentShares = current.Shares,
                    PreviousShares = previous?.Shares ?? 0,
                    CurrentValue = current.Value,
                    PreviousValue = previous?.Value ?? 0,
                    CurrentFilerCount = current.FilerCount,
                    PreviousFilerCount = previous?.FilerCount ?? 0,
                    ListingShares = listingShares,
                }
            );
        }
        return result;
    }

    // Combined-lane twin for the open filing window: the same per-stock figures with
    // non-filers carried forward at their prior-quarter positions, materialised by
    // HoldingsAggregateRefreshService while the window is open and deleted when it
    // closes. May be EMPTY during the transition (window just opened, first rebuild
    // not yet drained) — consumers must fall back to the live combined aggregation
    // when no rows match.
    public IQueryable<StockQuarterlyActivityCombined> GetStockActivitySnapshotsCombined(
        DateOnly reportDate
    ) => DbContext.Set<StockQuarterlyActivityCombined>().Where(s => s.ReportDate == reportDate);

    public IQueryable<StockQuarterlyListingActivity> GetStockListingActivitySnapshots(
        DateOnly reportDate,
        bool combined
    ) =>
        DbContext
            .Set<StockQuarterlyListingActivity>()
            .Where(row => row.ReportDate == reportDate && row.IsCombined == combined);

    // Loads one complete materialized market generation. Listing-share rows are part of the
    // snapshot contract: an older deployment can leave the new breakdown table empty while the
    // stock-level rows already exist, in which case callers must use the exact live fallback
    // until the one-time backfill publishes both tables atomically.
    public async Task<List<MarketWideStockActivity>> GetMarketActivitySnapshots(
        DateOnly reportDate,
        bool combined,
        CancellationToken cancellationToken = default
    )
    {
        List<MarketWideStockActivity> activity;
        Dictionary<Guid, DateTime> versions;
        if (combined)
        {
            var rows = await GetStockActivitySnapshotsCombined(reportDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            activity = rows.Select(row => row.ToActivity()).ToList();
            versions = rows.ToDictionary(row => row.CommonStockId, row => row.ComputedAt);
        }
        else
        {
            var rows = await GetStockActivitySnapshots(reportDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            activity = rows.Select(row => row.ToActivity()).ToList();
            versions = rows.ToDictionary(row => row.CommonStockId, row => row.ComputedAt);
        }
        if (activity.Count == 0)
            return [];

        var listingRows = await GetStockListingActivitySnapshots(reportDate, combined)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var byStock = listingRows
            .GroupBy(row => row.CommonStockId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StockQuarterlyListingActivity>)group.ToList()
            );
        if (
            activity.Any(row =>
                !byStock.TryGetValue(row.CommonStockId, out var listings)
                || listings.Any(listing => listing.ComputedAt != versions[row.CommonStockId])
            )
        )
            return [];

        foreach (var row in activity)
            row.ListingShares = byStock[row.CommonStockId];
        return activity;
    }

    // Snapshot-first complete market activity. Both the stock aggregate and exact-listing
    // breakdown are read under one repeatable snapshot; if either materialized slice is absent
    // (fresh database or additive-migration backfill), the same transaction supplies the bounded
    // two-quarter live fallback without mixing concurrent filing imports between statements.
    public async Task<List<MarketWideStockActivity>> GetMarketActivitySnapshotBacked(
        DateOnly current,
        DateOnly previous,
        bool combined,
        CancellationToken cancellationToken = default
    )
    {
        async Task<List<MarketWideStockActivity>> Load(CancellationToken operationCancellationToken)
        {
            var snapshot = await GetMarketActivitySnapshots(
                current,
                combined,
                operationCancellationToken
            );
            if (snapshot.Count > 0)
                return snapshot;

            var activity = await GetQuarterlyActivity(current, previous, combined)
                .ToListAsync(operationCancellationToken);
            var listings = await GetLiveListingShareActivity(
                current,
                previous,
                combined,
                activity.Select(row => row.CommonStockId).ToArray(),
                operationCancellationToken
            );
            var byStock = listings
                .GroupBy(row => row.CommonStockId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<StockQuarterlyListingActivity>)group.ToList()
                );
            if (activity.Any(row => !byStock.ContainsKey(row.CommonStockId)))
                return [];
            foreach (var row in activity)
                row.ListingShares = byStock[row.CommonStockId];
            return activity;
        }

        if (!DbContext.Database.IsRelational() || DbContext.Database.CurrentTransaction != null)
            return await Load(cancellationToken);

        var strategy = DbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async operationCancellationToken =>
            {
                await using var transaction = await DbContext.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.RepeatableRead,
                    operationCancellationToken
                );
                var result = await Load(operationCancellationToken);
                await transaction.CommitAsync(operationCancellationToken);
                return result;
            },
            cancellationToken
        );
    }

    // Exact listing grain for the rare live fallback while a snapshot generation is absent.
    // The query remains bounded to the requested quarter pair; normal request traffic reads the
    // much smaller StockQuarterlyListingActivity table instead.
    public async Task<List<StockQuarterlyListingActivity>> GetLiveListingShareActivity(
        DateOnly current,
        DateOnly previous,
        bool combined,
        IReadOnlyCollection<Guid> commonStockIds,
        CancellationToken cancellationToken = default
    )
    {
        if (commonStockIds.Count == 0)
            return [];

        if (!combined)
        {
            return await GetAll()
                .Where(Is13F)
                .Where(holding =>
                    commonStockIds.Contains(holding.CommonStockId)
                    && (holding.ReportDate == current || holding.ReportDate == previous)
                )
                .Join(
                    DbContext.Set<CommonStock>(),
                    holding => holding.CommonStockId,
                    stock => stock.Id,
                    (holding, stock) => new { Holding = holding, stock.Ticker }
                )
                .GroupBy(row => new
                {
                    row.Holding.CommonStockId,
                    PriceSeriesTicker = row.Holding.ListedTicker ?? row.Ticker,
                })
                .Select(group => new StockQuarterlyListingActivity
                {
                    CommonStockId = group.Key.CommonStockId,
                    ReportDate = current,
                    IsCombined = false,
                    PriceSeriesTicker = group.Key.PriceSeriesTicker,
                    CurrentShares = group.Sum(row =>
                        row.Holding.ReportDate == current ? row.Holding.Shares : 0L
                    ),
                    PreviousShares = group.Sum(row =>
                        row.Holding.ReportDate == previous ? row.Holding.Shares : 0L
                    ),
                })
                .ToListAsync(cancellationToken);
        }

        var currentRows = await GetCombinedQuarter(current, previous)
            .Where(holding => commonStockIds.Contains(holding.CommonStockId))
            .Join(
                DbContext.Set<CommonStock>(),
                holding => holding.CommonStockId,
                stock => stock.Id,
                (holding, stock) => new { Holding = holding, stock.Ticker }
            )
            .GroupBy(row => new
            {
                row.Holding.CommonStockId,
                PrimaryTicker = row.Ticker,
                PriceSeriesTicker = row.Holding.ListedTicker ?? row.Ticker,
                SourceReportDate = row.Holding.ReportDate,
            })
            .Select(group => new
            {
                group.Key.CommonStockId,
                group.Key.PrimaryTicker,
                group.Key.PriceSeriesTicker,
                group.Key.SourceReportDate,
                Shares = group.Sum(row => row.Holding.Shares),
            })
            .ToListAsync(cancellationToken);
        var previousRows = await GetAll()
            .Where(Is13F)
            .Where(holding =>
                commonStockIds.Contains(holding.CommonStockId) && holding.ReportDate == previous
            )
            .Join(
                DbContext.Set<CommonStock>(),
                holding => holding.CommonStockId,
                stock => stock.Id,
                (holding, stock) => new { Holding = holding, stock.Ticker }
            )
            .GroupBy(row => new
            {
                row.Holding.CommonStockId,
                PrimaryTicker = row.Ticker,
                PriceSeriesTicker = row.Holding.ListedTicker ?? row.Ticker,
            })
            .Select(group => new
            {
                group.Key.CommonStockId,
                group.Key.PrimaryTicker,
                group.Key.PriceSeriesTicker,
                Shares = group.Sum(row => row.Holding.Shares),
            })
            .ToListAsync(cancellationToken);
        var splitsByStock = (
            await DbContext
                .Set<StockSplit>()
                .AsNoTracking()
                .Where(split => commonStockIds.Contains(split.CommonStockId))
                .ToListAsync(cancellationToken)
        )
            .GroupBy(split => split.CommonStockId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<StockSplit>)group.ToList());
        var currentLookup = currentRows
            .GroupBy(row => new
            {
                row.CommonStockId,
                row.PrimaryTicker,
                row.PriceSeriesTicker,
            })
            .ToDictionary(
                group => (group.Key.CommonStockId, group.Key.PriceSeriesTicker),
                group =>
                    group.Sum(row =>
                        RestateShareCountBetween(
                            row.Shares,
                            row.SourceReportDate,
                            current,
                            group.Key.PrimaryTicker,
                            group.Key.PriceSeriesTicker,
                            splitsByStock.GetValueOrDefault(group.Key.CommonStockId) ?? []
                        )
                    )
            );
        var previousLookup = previousRows.ToDictionary(
            row => (row.CommonStockId, row.PriceSeriesTicker),
            row => row.Shares
        );
        return currentLookup
            .Keys.Union(previousLookup.Keys)
            .Select(key => new StockQuarterlyListingActivity
            {
                CommonStockId = key.CommonStockId,
                ReportDate = current,
                IsCombined = true,
                PriceSeriesTicker = key.PriceSeriesTicker,
                CurrentShares = currentLookup.GetValueOrDefault(key),
                PreviousShares = previousLookup.GetValueOrDefault(key),
            })
            .ToList();
    }

    private static long RestateShareCountBetween(
        long shares,
        DateOnly sourceReportDate,
        DateOnly targetReportDate,
        string primaryTicker,
        string priceSeriesTicker,
        IReadOnlyList<StockSplit> splits
    )
    {
        if (sourceReportDate >= targetReportDate || splits.Count == 0)
            return shares;

        var scoped = PriceSeriesSplitScope.ForListing(splits, primaryTicker, priceSeriesTicker);
        var sourceFactor = SplitAdjustment.ShareCountFactor(sourceReportDate, scoped);
        var targetFactor = SplitAdjustment.ShareCountFactor(targetReportDate, scoped);
        return targetFactor == 0m
            ? shares
            : SplitAdjustment.AdjustShareCount(shares, sourceFactor / targetFactor);
    }

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
    // The filing rollup rows behind one (holder, quarter) — usually a single filing, several
    // when the quarter was amended. Callers wanting "what did the filer declare for this
    // quarter" take the latest FilingDate: a restatement's declaration supersedes the
    // original's, and for the ordinary one-filing case it is simply that filing.
    public IQueryable<InstitutionalFiling> GetFilingsByHolder(
        InstitutionalHolder holder,
        DateOnly reportDate
    )
    {
        return DbContext
            .Set<InstitutionalFiling>()
            .Where(f => f.InstitutionalHolderId == holder.Id && f.ReportDate == reportDate);
    }

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
    //
    // splitAffectedStockIds: stocks with a split effective after `current` — their as-filed
    // CurrentShares sits on a different basis than today's SharesOutStanding, so the SQL
    // % of float predicates PASS them THROUGH instead of judging them on a wrong ratio;
    // ScreenRestated re-judges them in memory on the restated count. Null/empty keeps the
    // pure-SQL behaviour.
    public IQueryable<ScreenerRow> Screen(
        ScreenerCriteria criteria,
        DateOnly current,
        DateOnly previous,
        IReadOnlyCollection<Guid> splitAffectedStockIds = null
    )
    {
        var pctPassthroughIds = splitAffectedStockIds is { Count: > 0 }
            ? splitAffectedStockIds.ToList()
            : null;
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
                criteria.MinPctFloat.HasValue && pctPassthroughIds == null,
                j =>
                    j.cs.SharesOutStanding > 0
                    && (double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0
                        >= criteria.MinPctFloat.Value
            )
            .WhereIf(
                criteria.MinPctFloat.HasValue && pctPassthroughIds != null,
                j =>
                    pctPassthroughIds.Contains(j.agg.CommonStockId)
                    || (
                        j.cs.SharesOutStanding > 0
                        && (double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0
                            >= criteria.MinPctFloat.Value
                    )
            )
            .WhereIf(
                criteria.MaxPctFloat.HasValue && pctPassthroughIds == null,
                j =>
                    j.cs.SharesOutStanding > 0
                    && (double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0
                        <= criteria.MaxPctFloat.Value
            )
            .WhereIf(
                criteria.MaxPctFloat.HasValue && pctPassthroughIds != null,
                j =>
                    pctPassthroughIds.Contains(j.agg.CommonStockId)
                    || (
                        j.cs.SharesOutStanding > 0
                        && (double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0
                            <= criteria.MaxPctFloat.Value
                    )
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

    // Split-aware screener entry point: runs Screen with % of float passthrough for stocks
    // that split after `current`, restates those stocks' CurrentShares / PercentOfFloat onto
    // today's basis from their exact per-listing sums, re-applies the % filters on the
    // restated ratio, and caps the ordered result. Ordering is by CurrentValue (a dollar
    // figure, split-invariant), so the SQL Take fast path stays correct whenever no
    // in-memory % re-judgement is needed.
    //
    // splitsSinceCurrent: splits effective strictly after `current` and on or before today
    // (load via StockSplitRepository.GetEffective(today) — announced-but-not-effective rows
    // must never restate anything).
    public async Task<List<ScreenerRow>> ScreenRestated(
        ScreenerCriteria criteria,
        DateOnly current,
        DateOnly previous,
        IReadOnlyList<StockSplit> splitsSinceCurrent,
        int? rowCap,
        CancellationToken cancellationToken = default
    )
    {
        var splitsByStock = (splitsSinceCurrent ?? [])
            .Where(s => s.Numerator > 0 && s.Denominator > 0)
            .GroupBy(s => s.CommonStockId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StockSplit>)g.ToList());

        var query = Screen(criteria, current, previous, splitsByStock.Keys.ToList())
            .OrderByDescending(r => r.CurrentValue);

        // The SQL % predicates passed affected stocks through un-judged, so the cap can
        // only apply after the in-memory re-judgement; without an active % filter the
        // passthrough changes nothing and the SQL Take fast path is exact.
        var pctFilterActive = criteria.MinPctFloat.HasValue || criteria.MaxPctFloat.HasValue;
        var needsInMemoryPctPass = splitsByStock.Count > 0 && pctFilterActive;

        var rows =
            rowCap.HasValue && !needsInMemoryPctPass
                ? await query.Take(rowCap.Value).ToListAsync(cancellationToken)
                : await query.ToListAsync(cancellationToken);

        var affectedRows = rows.Where(r => splitsByStock.ContainsKey(r.CommonStockId)).ToList();
        if (affectedRows.Count > 0)
        {
            var affectedIds = affectedRows.Select(r => r.CommonStockId).Distinct().ToList();
            var listingShares = await BothQuarters(current, previous)
                .Where(h => h.ReportDate == current && affectedIds.Contains(h.CommonStockId))
                .GroupBy(h => new { h.CommonStockId, h.ListedTicker })
                .Select(g => new ScreenerListingShares
                {
                    CommonStockId = g.Key.CommonStockId,
                    ListedTicker = g.Key.ListedTicker,
                    Shares = g.Sum(h => h.Shares),
                })
                .ToListAsync(cancellationToken);
            var listingSharesByStock = listingShares
                .GroupBy(s => s.CommonStockId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ScreenerListingShares>)g.ToList());

            foreach (var row in affectedRows)
                ScreenerSplitRestatement.RestateRow(
                    row,
                    listingSharesByStock.GetValueOrDefault(row.CommonStockId) ?? [],
                    splitsByStock[row.CommonStockId],
                    current
                );

            if (needsInMemoryPctPass)
                rows = rows.Where(r =>
                        !splitsByStock.ContainsKey(r.CommonStockId)
                        || ScreenerSplitRestatement.PassesPctFloat(r, criteria)
                    )
                    .ToList();
        }

        return rowCap.HasValue && rows.Count > rowCap.Value
            ? rows.Take(rowCap.Value).ToList()
            : rows;
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
        // The distinct filer universe of the combined view is exactly the union of
        // current- and previous-quarter 13F filers. Applying the per-row carry-forward
        // NOT-EXISTS predicate before DISTINCT does not change that set, but it turns a
        // sub-second indexed union into a corpus-wide correlated scan at production scale.
        return GetAll()
            .Where(Is13F)
            .Where(h => h.ReportDate == current || h.ReportDate == previous)
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
