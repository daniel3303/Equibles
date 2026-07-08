using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class ListedSecurityRepository : BaseRepository<ListedSecurity>
{
    public ListedSecurityRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<ListedSecurity> GetByStock(CommonStock stock)
    {
        return GetAll().Where(s => s.CommonStockId == stock.Id);
    }
}
