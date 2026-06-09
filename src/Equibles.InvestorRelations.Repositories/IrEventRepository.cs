using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InvestorRelations.Data.Models;

namespace Equibles.InvestorRelations.Repositories;

public class IrEventRepository : BaseRepository<IrEvent>
{
    public IrEventRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<IrEvent> GetByStock(CommonStock stock)
    {
        return GetAll()
            .Where(e => e.CommonStockId == stock.Id)
            .OrderByDescending(e => e.ScheduledDate);
    }

    public IQueryable<IrEvent> GetUpcoming(CommonStock stock, DateTime asOf)
    {
        return GetAll()
            .Where(e => e.CommonStockId == stock.Id && e.ScheduledDate >= asOf)
            .OrderBy(e => e.ScheduledDate);
    }

    public IQueryable<IrEvent> GetByStockTitleAndDate(
        CommonStock stock,
        string title,
        DateTime scheduledDate
    )
    {
        return GetAll()
            .Where(e =>
                e.CommonStockId == stock.Id && e.Title == title && e.ScheduledDate == scheduledDate
            );
    }
}
