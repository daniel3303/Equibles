using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Yahoo.Data.Models;

namespace Equibles.Yahoo.Repositories;

public class DailyStockPriceRepository : BaseRepository<DailyStockPrice>
{
    public DailyStockPriceRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<DailyStockPrice> GetByStock(CommonStock stock)
    {
        return GetAll().Where(p => p.CommonStockId == stock.Id);
    }

    public IQueryable<DailyStockPrice> GetByStock(
        CommonStock stock,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetAll()
            .Where(p => p.CommonStockId == stock.Id && p.Date >= startDate && p.Date <= endDate);
    }

    public IQueryable<DailyStockPrice> GetByStocks(
        IEnumerable<Guid> stockIds,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetAll()
            .Where(p =>
                stockIds.Contains(p.CommonStockId) && p.Date >= startDate && p.Date <= endDate
            );
    }

    public IQueryable<DateOnly> GetLatestDate(CommonStock stock)
    {
        return GetAll().Where(p => p.CommonStockId == stock.Id).LatestValue(p => p.Date);
    }

    public IQueryable<DateOnly> GetLatestDateAcrossAllStocks()
    {
        return GetAll().LatestValue(p => p.Date, distinct: true);
    }
}
