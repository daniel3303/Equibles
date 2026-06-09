using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InvestorRelations.Data.Models;

namespace Equibles.InvestorRelations.Repositories;

public class IrNewsItemRepository : BaseRepository<IrNewsItem>
{
    public IrNewsItemRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<IrNewsItem> GetByStock(CommonStock stock)
    {
        return GetAll()
            .Where(n => n.CommonStockId == stock.Id)
            .OrderByDescending(n => n.PublishedDate);
    }

    public IQueryable<IrNewsItem> GetByStockAndUrl(CommonStock stock, string url)
    {
        return GetAll().Where(n => n.CommonStockId == stock.Id && n.Url == url);
    }
}
