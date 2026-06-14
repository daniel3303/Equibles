using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Finra.Data.Models;

namespace Equibles.Finra.Repositories;

public class DailyShortVolumeRepository : BaseRepository<DailyShortVolume>
{
    public DailyShortVolumeRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<DailyShortVolume> GetByStock(CommonStock stock, DateOnly date)
    {
        return GetAll().Where(d => d.CommonStockId == stock.Id && d.Date == date);
    }

    public IQueryable<DailyShortVolume> GetHistoryByStock(CommonStock stock)
    {
        return GetAll().Where(d => d.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestDate()
    {
        return GetAll().LatestValue(d => d.Date, distinct: true);
    }

    public IQueryable<DateOnly> GetEarliestDate()
    {
        return GetAll().Select(d => d.Date).Distinct().OrderBy(d => d).Take(1);
    }

    public IQueryable<DailyShortVolume> GetByDate(DateOnly date)
    {
        return GetAll().Where(d => d.Date == date);
    }
}
