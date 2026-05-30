using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.Repositories;

public class Form144FilingRepository : BaseRepository<Form144Filing>
{
    public Form144FilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<Form144Filing> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<Form144Filing> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(f => f.AccessionNumber == accessionNumber);
    }

    public IQueryable<Form144Filing> GetRecent(DateOnly since)
    {
        return GetAll().Where(f => f.FilingDate >= since);
    }
}
