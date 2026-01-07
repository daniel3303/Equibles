using Equibles.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class InstitutionalHoldingRepository : BaseRepository<InstitutionalHolding> {
    public InstitutionalHoldingRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<InstitutionalHolding> GetByStock(CommonStock stock, DateOnly reportDate) {
        return GetAll().Where(h => h.CommonStockId == stock.Id && h.ReportDate == reportDate);
    }

    public IQueryable<InstitutionalHolding> GetByHolder(InstitutionalHolder holder, DateOnly reportDate) {
        return GetAll().Where(h => h.InstitutionalHolderId == holder.Id && h.ReportDate == reportDate);
    }

    public IQueryable<InstitutionalHolding> GetHistoryByStock(CommonStock stock) {
        return GetAll().Where(h => h.CommonStockId == stock.Id);
    }

    public IQueryable<InstitutionalHolding> GetHistoryByHolder(InstitutionalHolder holder) {
        return GetAll().Where(h => h.InstitutionalHolderId == holder.Id);
    }

    public IQueryable<DateOnly> GetAvailableReportDates() {
        return GetAll().Select(h => h.ReportDate).Distinct();
    }

    public IQueryable<InstitutionalHolding> GetByAccessionNumber(string accessionNumber) {
        return GetAll().Where(h => h.AccessionNumber == accessionNumber);
    }
}
