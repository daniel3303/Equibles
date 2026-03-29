using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Yahoo.Data.Models;

namespace Equibles.Yahoo.Repositories;

public class DailyStockPriceRepository : BaseRepository<DailyStockPrice> {
    public DailyStockPriceRepository(EquiblesDbContext dbContext) : base(dbContext) { }

    public IQueryable<DailyStockPrice> GetByStock(CommonStock stock) {
        return GetAll().Where(p => p.CommonStockId == stock.Id);
    }

    public IQueryable<DailyStockPrice> GetByStock(CommonStock stock, DateOnly startDate, DateOnly endDate) {
        return GetAll().Where(p => p.CommonStockId == stock.Id && p.Date >= startDate && p.Date <= endDate);
    }

    public IQueryable<DateOnly> GetLatestDate(CommonStock stock) {
        return GetAll()
            .Where(p => p.CommonStockId == stock.Id)
            .Select(p => p.Date)
            .OrderByDescending(d => d)
            .Take(1);
    }

    public IQueryable<DateOnly> GetLatestDateAcrossAllStocks() {
        return GetAll()
            .Select(p => p.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(1);
    }
}
