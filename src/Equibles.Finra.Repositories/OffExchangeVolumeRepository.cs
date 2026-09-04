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
        return GetAll().Where(d =>
            d.CommonStockId == stock.Id
            && (d.ListedTicker == stock.Ticker || d.ListedTicker == "")
            && d.WeekStartDate == weekStartDate
        );
    }

    public IQueryable<OffExchangeVolume> GetByListing(
        CommonStock stock,
        string listedTicker,
        DateOnly weekStartDate
    )
    {
        var isPrimary = string.Equals(listedTicker, stock.Ticker, StringComparison.OrdinalIgnoreCase);
        return GetAll().Where(d =>
            d.CommonStockId == stock.Id
            && (d.ListedTicker == listedTicker || (isPrimary && d.ListedTicker == ""))
            && d.WeekStartDate == weekStartDate
        );
    }

    public IQueryable<OffExchangeVolume> GetHistoryByStock(CommonStock stock)
    {
        return GetAll().Where(d =>
            d.CommonStockId == stock.Id
            && (d.ListedTicker == stock.Ticker || d.ListedTicker == "")
        );
    }

    public IQueryable<OffExchangeVolume> GetHistoryByListing(CommonStock stock, string listedTicker)
    {
        var isPrimary = string.Equals(listedTicker, stock.Ticker, StringComparison.OrdinalIgnoreCase);
        return GetAll().Where(d =>
            d.CommonStockId == stock.Id
            && (d.ListedTicker == listedTicker || (isPrimary && d.ListedTicker == ""))
        );
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
