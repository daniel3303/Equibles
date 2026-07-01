using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;

namespace Equibles.CorporateActions.Repositories;

public class CashDividendRepository : BaseRepository<CashDividend>
{
    public CashDividendRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CashDividend> GetByStock(Guid commonStockId)
    {
        return GetAll().Where(d => d.CommonStockId == commonStockId);
    }
}
