using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class FailToDeliverRepository : BaseRepository<FailToDeliver>
{
    public FailToDeliverRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FailToDeliver> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestDate()
    {
        return GetAll().LatestValue(f => f.SettlementDate, distinct: true);
    }
}
