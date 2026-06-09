using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class EarningsCalendarEntryRepository : BaseRepository<EarningsCalendarEntry>
{
    public EarningsCalendarEntryRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>A stock's earnings calendar entries, soonest scheduled date first.</summary>
    public IQueryable<EarningsCalendarEntry> GetByStock(CommonStock stock)
    {
        return GetAll().Where(e => e.CommonStockId == stock.Id).OrderBy(e => e.ScheduledDate);
    }
}
