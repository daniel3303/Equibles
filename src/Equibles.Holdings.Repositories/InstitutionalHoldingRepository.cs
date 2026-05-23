using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories;

public class InstitutionalHoldingRepository : BaseRepository<InstitutionalHolding>
{
    public InstitutionalHoldingRepository(EquiblesDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<InstitutionalHolding> GetByStock(CommonStock stock, DateOnly reportDate)
    {
        return GetAll().Where(h => h.CommonStockId == stock.Id && h.ReportDate == reportDate);
    }

    public IQueryable<InstitutionalHolding> GetByHolder(
        InstitutionalHolder holder,
        DateOnly reportDate
    )
    {
        return GetAll()
            .Where(h => h.InstitutionalHolderId == holder.Id && h.ReportDate == reportDate);
    }

    public IQueryable<InstitutionalHolding> GetHistoryByStock(CommonStock stock)
    {
        return GetAll().Where(h => h.CommonStockId == stock.Id);
    }

    public IQueryable<InstitutionalHolding> GetHistoryByHolder(InstitutionalHolder holder)
    {
        return GetAll().Where(h => h.InstitutionalHolderId == holder.Id);
    }

    public IQueryable<DateOnly> GetAvailableReportDates()
    {
        return GetAll().Select(h => h.ReportDate).Distinct();
    }

    public IQueryable<InstitutionalHolding> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(h => h.AccessionNumber == accessionNumber);
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
        return GetAll()
            .Where(h => h.ReportDate == current || h.ReportDate == previous)
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
        return GetAll()
            .Where(h => h.ReportDate == current || h.ReportDate == previous)
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
        var aggregated = GetAll()
            .Where(h => h.ReportDate == current || h.ReportDate == previous)
            .GroupBy(h => new { h.InstitutionalHolderId, h.CommonStockId })
            .Select(g => new
            {
                g.Key.InstitutionalHolderId,
                g.Key.CommonStockId,
                CurrentShares = g.Sum(h => h.ReportDate == current ? h.Shares : 0L),
                PreviousShares = g.Sum(h => h.ReportDate == previous ? h.Shares : 0L),
                CurrentValue = g.Sum(h => h.ReportDate == current ? h.Value : 0L),
                PreviousValue = g.Sum(h => h.ReportDate == previous ? h.Value : 0L),
            })
            .Where(a => a.PreviousShares > 0 && a.CurrentShares > a.PreviousShares)
            .Where(a =>
                (double)(a.CurrentShares - a.PreviousShares) / a.PreviousShares * 100.0
                >= minPctIncrease
            );

        return aggregated
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
    }

    public IQueryable<AumSnapshot> GetAumByReportDate()
    {
        return GetAll()
            .GroupBy(h => h.ReportDate)
            .Select(g => new AumSnapshot
            {
                ReportDate = g.Key,
                TotalValue = g.Sum(h => h.Value),
                FilerCount = g.Select(h => h.InstitutionalHolderId).Distinct().Count(),
                PositionCount = g.Count(),
                StockCount = g.Select(h => h.CommonStockId).Distinct().Count(),
                FilingCount = g.Select(h => h.AccessionNumber).Distinct().Count(),
            });
    }

    public IQueryable<SectorAllocationSnapshot> GetSectorAllocationByReportDate()
    {
        var aggregated = GetAll()
            .Join(
                DbContext.Set<CommonStock>(),
                h => h.CommonStockId,
                s => s.Id,
                (h, s) =>
                    new
                    {
                        h.ReportDate,
                        h.Value,
                        s.IndustryId,
                    }
            )
            .Join(
                DbContext.Set<CommonStocks.Data.Models.Taxonomies.Industry>(),
                x => x.IndustryId,
                i => i.Id,
                (x, i) =>
                    new
                    {
                        x.ReportDate,
                        x.Value,
                        i.SectorId,
                    }
            )
            .Where(x => x.SectorId != null)
            .GroupBy(x => new { x.ReportDate, SectorId = (Guid)x.SectorId })
            .Select(g => new
            {
                g.Key.ReportDate,
                g.Key.SectorId,
                TotalValue = g.Sum(x => x.Value),
            });

        return aggregated.Join(
            DbContext.Set<CommonStocks.Data.Models.Taxonomies.Sector>(),
            a => a.SectorId,
            s => s.Id,
            (a, s) =>
                new SectorAllocationSnapshot
                {
                    ReportDate = a.ReportDate,
                    SectorId = a.SectorId,
                    SectorName = s.Name,
                    TotalValue = a.TotalValue,
                }
        );
    }

    // Recent filings feed: groups holdings by accession number to produce one row
    // per filing, ordered by import timestamp. Joins InstitutionalHolder for filer
    // metadata and uses a NOT EXISTS subquery to flag first-time filers (no holdings
    // from any earlier report date).
    public IQueryable<RecentFiling> GetRecentFilings()
    {
        var filings = GetAll()
            .GroupBy(h => new
            {
                h.AccessionNumber,
                h.InstitutionalHolderId,
                h.FilingDate,
                h.ReportDate,
                h.IsAmendment,
            })
            .Select(g => new
            {
                g.Key.AccessionNumber,
                g.Key.InstitutionalHolderId,
                g.Key.FilingDate,
                g.Key.ReportDate,
                g.Key.IsAmendment,
                PositionCount = g.Count(),
                TotalValue = g.Sum(h => h.Value),
                ImportedAt = g.Min(h => h.CreationTime),
            });

        return filings.Join(
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
                    ImportedAt = f.ImportedAt,
                    IsNewFiler = !DbContext
                        .Set<InstitutionalHolding>()
                        .Any(prior =>
                            prior.InstitutionalHolderId == f.InstitutionalHolderId
                            && prior.ReportDate < f.ReportDate
                        ),
                }
        );
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
        var aggregated = GetAll()
            .Where(h => h.ReportDate == current || h.ReportDate == previous)
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

        if (criteria.MinFilerCount.HasValue)
            aggregated = aggregated.Where(r => r.CurrentFilerCount >= criteria.MinFilerCount.Value);
        if (criteria.MaxFilerCount.HasValue)
            aggregated = aggregated.Where(r => r.CurrentFilerCount <= criteria.MaxFilerCount.Value);
        if (criteria.MinDeltaFilerCount.HasValue)
            aggregated = aggregated.Where(r =>
                r.CurrentFilerCount - r.PreviousFilerCount >= criteria.MinDeltaFilerCount.Value
            );
        if (criteria.MaxDeltaFilerCount.HasValue)
            aggregated = aggregated.Where(r =>
                r.CurrentFilerCount - r.PreviousFilerCount <= criteria.MaxDeltaFilerCount.Value
            );
        if (criteria.MinTotalValue.HasValue)
            aggregated = aggregated.Where(r => r.CurrentValue >= criteria.MinTotalValue.Value);
        if (criteria.MaxTotalValue.HasValue)
            aggregated = aggregated.Where(r => r.CurrentValue <= criteria.MaxTotalValue.Value);
        if (criteria.MinDeltaValue.HasValue)
            aggregated = aggregated.Where(r =>
                r.CurrentValue - r.PreviousValue >= criteria.MinDeltaValue.Value
            );
        if (criteria.MaxDeltaValue.HasValue)
            aggregated = aggregated.Where(r =>
                r.CurrentValue - r.PreviousValue <= criteria.MaxDeltaValue.Value
            );
        if (criteria.MinNewPositions.HasValue)
            aggregated = aggregated.Where(r => r.NewFilerCount >= criteria.MinNewPositions.Value);
        if (criteria.MinSoldOutPositions.HasValue)
            aggregated = aggregated.Where(r =>
                r.SoldOutFilerCount >= criteria.MinSoldOutPositions.Value
            );

        var joined = aggregated.Join(
            DbContext.Set<CommonStock>(),
            agg => agg.CommonStockId,
            cs => cs.Id,
            (agg, cs) => new { agg, cs }
        );

        if (criteria.IndustryIds.Count > 0)
            joined = joined.Where(j =>
                j.cs.IndustryId != null && criteria.IndustryIds.Contains(j.cs.IndustryId.Value)
            );

        // SharesOutStanding == 0 means "unknown" in the current schema; any % filter
        // excludes those stocks rather than treating them as 100% held (false positive).
        if (criteria.MinPctFloat.HasValue)
        {
            var min = criteria.MinPctFloat.Value;
            joined = joined.Where(j =>
                j.cs.SharesOutStanding > 0
                && (double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0 >= min
            );
        }
        if (criteria.MaxPctFloat.HasValue)
        {
            var max = criteria.MaxPctFloat.Value;
            joined = joined.Where(j =>
                j.cs.SharesOutStanding > 0
                && (double)j.agg.CurrentShares / j.cs.SharesOutStanding * 100.0 <= max
            );
        }

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
}
