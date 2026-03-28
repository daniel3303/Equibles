using Equibles.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.ShortData.Data.Models;

namespace Equibles.ShortData.Repositories;

public class DailyShortVolumeRepository : BaseRepository<DailyShortVolume> {
    public DailyShortVolumeRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<DailyShortVolume> GetByStock(CommonStock stock, DateOnly date) {
        return GetAll().Where(d => d.CommonStockId == stock.Id && d.Date == date);
    }

    public IQueryable<DailyShortVolume> GetHistoryByStock(CommonStock stock) {
        return GetAll().Where(d => d.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestDate() {
        return GetAll().Select(d => d.Date).Distinct().OrderByDescending(d => d).Take(1);
    }

    public IQueryable<DailyShortVolume> GetByDate(DateOnly date) {
        return GetAll().Where(d => d.Date == date);
    }
}
