using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class NportFilingRepository : BaseRepository<NportFiling>
{
    public NportFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<NportFiling> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<NportFiling> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(f => f.AccessionNumber == accessionNumber);
    }

    public IQueryable<NportFiling> GetRecent(DateOnly since)
    {
        return GetAll().Where(f => f.FilingDate >= since);
    }
}
