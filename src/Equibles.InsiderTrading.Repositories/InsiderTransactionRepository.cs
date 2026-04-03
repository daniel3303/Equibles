using Equibles.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Repositories;

public class InsiderTransactionRepository : BaseRepository<InsiderTransaction> {
    public InsiderTransactionRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<InsiderTransaction> GetByStock(CommonStock stock) {
        return GetAll().Where(t => t.CommonStockId == stock.Id);
    }

    public IQueryable<InsiderTransaction> GetByStock(CommonStock stock, DateOnly from, DateOnly to) {
        return GetAll().Where(t => t.CommonStockId == stock.Id && t.TransactionDate >= from && t.TransactionDate <= to);
    }

    public IQueryable<InsiderTransaction> GetByOwner(InsiderOwner owner) {
        return GetAll().Where(t => t.InsiderOwnerId == owner.Id);
    }

    public IQueryable<InsiderTransaction> GetHistoryByStock(CommonStock stock) {
        return GetAll().Where(t => t.CommonStockId == stock.Id);
    }

    public IQueryable<InsiderTransaction> GetByAccessionNumber(string accessionNumber) {
        return GetAll().Where(t => t.AccessionNumber == accessionNumber);
    }

}
