using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class FormDFilingRepository : BaseRepository<FormDFiling>
{
    public FormDFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FormDFiling> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<FormDFiling> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(f => f.AccessionNumber == accessionNumber);
    }

    public IQueryable<FormDFiling> GetRecent(DateOnly since)
    {
        return GetAll().Where(f => f.FilingDate >= since);
    }
}
