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
        return GetHistoryByHolder(holder).Where(h => h.FilingType == FilingType.Form13F);
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
        GetAll().Where(h => h.FilingType == FilingType.Form13F).DistinctReportDatesDescending();

    // Latest dates first — see GetAvailableReportDates for the ordering contract.
    public IQueryable<DateOnly> GetReportDatesByStock(CommonStock stock) =>
        GetHistoryByStock(stock).DistinctReportDatesDescending();

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

    // The two-quarter comparison window shared by every per-stock activity / churn /
    // double-down aggregate: both report dates are pulled in a single round trip and
    // each caller applies its own GROUP BY downstream.
    private IQueryable<InstitutionalHolding> BothQuarters(DateOnly current, DateOnly previous) =>
        GetAll().Where(h => h.ReportDate == current || h.ReportDate == previous);

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
    // for the "% of 13F universe" column on the most-held page.
    public IQueryable<Guid> GetUniqueFilerIds(DateOnly reportDate)
    {
        return GetAll()
            .Where(h => h.ReportDate == reportDate)
            .Select(h => h.InstitutionalHolderId)
            .Distinct();
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
        return GetAll()
            .Where(h => h.CommonStockId == stock.Id && ids.Contains(h.InstitutionalHolderId))
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new KeyValuePair<Guid, DateOnly>(g.Key, g.Min(h => h.ReportDate)));
    }

    // "Current combined" quarter: best-available holdings per holder. Uses current-
    // quarter data for holders who already filed, falls back to the previous quarter
    // for holders who haven't. The NOT EXISTS subquery identifies non-filers.
    public IQueryable<InstitutionalHolding> GetCombinedQuarter(DateOnly current, DateOnly previous)
    {
        return GetAll()
            .Where(h =>
                h.ReportDate == current
                || (
                    h.ReportDate == previous
                    && !DbContext
                        .Set<InstitutionalHolding>()
                        .Any(c =>
                            c.ReportDate == current
                            && c.InstitutionalHolderId == h.InstitutionalHolderId
                        )
                )
            );
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
                                && c.InstitutionalHolderId == h.InstitutionalHolderId
                            )
                        && !DbContext
                            .Set<InstitutionalHolding>()
                            .Any(c =>
                                c.ReportDate == current
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
