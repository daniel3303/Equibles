using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialFactRepository : BaseRepository<FinancialFact>
{
    public FinancialFactRepository(EquiblesDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FinancialFact> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }
}
