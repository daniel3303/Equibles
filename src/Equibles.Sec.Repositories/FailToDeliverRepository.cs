using Equibles.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class FailToDeliverRepository : BaseRepository<FailToDeliver> {
    public FailToDeliverRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<FailToDeliver> GetByStock(CommonStock stock) {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestDate() {
        return GetAll().Select(f => f.SettlementDate).Distinct().OrderByDescending(d => d).Take(1);
    }
}
