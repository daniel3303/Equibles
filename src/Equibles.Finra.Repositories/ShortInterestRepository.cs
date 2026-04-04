using Equibles.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.Data.Models;

namespace Equibles.Finra.Repositories;

public class ShortInterestRepository : BaseRepository<ShortInterest> {
    public ShortInterestRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<ShortInterest> GetByStock(CommonStock stock, DateOnly settlementDate) {
        return GetAll().Where(s => s.CommonStockId == stock.Id && s.SettlementDate == settlementDate);
    }

    public IQueryable<ShortInterest> GetHistoryByStock(CommonStock stock) {
        return GetAll().Where(s => s.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestSettlementDate() {
        return GetAll().Select(s => s.SettlementDate).Distinct().OrderByDescending(d => d).Take(1);
    }

    public IQueryable<ShortInterest> GetBySettlementDate(DateOnly settlementDate) {
        return GetAll().Where(s => s.SettlementDate == settlementDate);
    }

    public IQueryable<DateOnly> GetAllSettlementDates() {
        return GetAll().Select(s => s.SettlementDate).Distinct();
    }

    public IQueryable<Guid> GetStockIdsBySettlementDate(DateOnly settlementDate) {
        return GetAll()
            .Where(s => s.SettlementDate == settlementDate)
            .Select(s => s.CommonStockId);
    }
}
