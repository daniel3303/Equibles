using Equibles.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;

namespace Equibles.Congress.Repositories;

public class CongressionalTradeRepository : BaseRepository<CongressionalTrade> {
    public CongressionalTradeRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<CongressionalTrade> GetByStock(CommonStock stock) {
        return GetAll().Where(t => t.CommonStockId == stock.Id);
    }

    public IQueryable<CongressionalTrade> GetByStock(CommonStock stock, DateOnly from, DateOnly to) {
        return GetAll().Where(t => t.CommonStockId == stock.Id && t.TransactionDate >= from && t.TransactionDate <= to);
    }

    public IQueryable<CongressionalTrade> GetByMember(CongressMember member) {
        return GetAll().Where(t => t.CongressMemberId == member.Id);
    }
}
