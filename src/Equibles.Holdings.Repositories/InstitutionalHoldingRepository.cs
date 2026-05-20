using Equibles.CommonStocks.Data.Models;
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
}
