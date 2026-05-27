using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialFactsSyncStatusRepository : BaseRepository<FinancialFactsSyncStatus>
{
    public FinancialFactsSyncStatusRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FinancialFactsSyncStatus> GetByStock(CommonStock stock)
    {
        return GetAll().Where(s => s.CommonStockId == stock.Id);
    }
}
