using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class IrNewsItemRepository : BaseRepository<IrNewsItem>
{
    public IrNewsItemRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>A stock's IR news items, newest first.</summary>
    public IQueryable<IrNewsItem> GetByStock(CommonStock stock)
    {
        return GetAll()
            .Where(n => n.CommonStockId == stock.Id)
            .OrderByDescending(n => n.PublishedAt);
    }
}
