using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;

namespace Equibles.CorporateActions.Repositories;

public class StockSplitRepository : BaseRepository<StockSplit>
{
    public StockSplitRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<StockSplit> GetByStock(Guid commonStockId)
    {
        return GetAll().Where(s => s.CommonStockId == commonStockId);
    }

    public IQueryable<StockSplit> GetPendingPriceAdjustment()
    {
        return GetAll().Where(s => s.PriceAdjustmentAppliedTime == null);
    }
}
