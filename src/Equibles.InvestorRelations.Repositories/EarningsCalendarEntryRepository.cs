using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InvestorRelations.Data.Models;

namespace Equibles.InvestorRelations.Repositories;

public class EarningsCalendarEntryRepository : BaseRepository<EarningsCalendarEntry>
{
    public EarningsCalendarEntryRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<EarningsCalendarEntry> GetByStock(CommonStock stock)
    {
        return GetAll()
            .Where(e => e.CommonStockId == stock.Id)
            .OrderByDescending(e => e.ScheduledDate);
    }

    public IQueryable<EarningsCalendarEntry> GetUpcoming(CommonStock stock, DateOnly asOf)
    {
        return GetAll()
            .Where(e => e.CommonStockId == stock.Id && e.ScheduledDate >= asOf)
            .OrderBy(e => e.ScheduledDate);
    }

    public IQueryable<EarningsCalendarEntry> GetByStockAndDate(CommonStock stock, DateOnly date)
    {
        return GetAll().Where(e => e.CommonStockId == stock.Id && e.ScheduledDate == date);
    }
}
