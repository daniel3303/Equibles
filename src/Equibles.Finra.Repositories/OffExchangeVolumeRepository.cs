using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Finra.Data.Models;

namespace Equibles.Finra.Repositories;

public class OffExchangeVolumeRepository : BaseRepository<OffExchangeVolume>
{
    public OffExchangeVolumeRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<OffExchangeVolume> GetByStock(CommonStock stock, DateOnly weekStartDate)
    {
        return GetAll().Where(d => d.CommonStockId == stock.Id && d.WeekStartDate == weekStartDate);
    }

    public IQueryable<OffExchangeVolume> GetHistoryByStock(CommonStock stock)
    {
        return GetAll().Where(d => d.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestWeek()
    {
        return GetAll().LatestValue(d => d.WeekStartDate, distinct: true);
    }

    public IQueryable<DateOnly> GetEarliestWeek()
    {
        return GetAll().Select(d => d.WeekStartDate).Distinct().OrderBy(d => d).Take(1);
    }

    public IQueryable<OffExchangeVolume> GetByWeek(DateOnly weekStartDate)
    {
        return GetAll().Where(d => d.WeekStartDate == weekStartDate);
    }
}
