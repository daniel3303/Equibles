using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class IrEventRepository : BaseRepository<IrEvent>
{
    public IrEventRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>A stock's IR events, soonest first.</summary>
    public IQueryable<IrEvent> GetByStock(CommonStock stock)
    {
        return GetAll().Where(e => e.CommonStockId == stock.Id).OrderBy(e => e.StartDateTime);
    }
}
