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
    // Sold-out filer-count metrics need a set-difference between holders per stock per
    // quarter and live in a separate query — see GetQuarterlyNewSoldOut (TODO #1009).
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
}
