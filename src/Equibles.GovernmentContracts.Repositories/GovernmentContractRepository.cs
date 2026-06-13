using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.GovernmentContracts.Data.Models;

namespace Equibles.GovernmentContracts.Repositories;

public class GovernmentContractRepository : BaseRepository<GovernmentContract>
{
    public GovernmentContractRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<GovernmentContract> GetByCommonStock(CommonStock commonStock)
    {
        return GetAll().Where(c => c.CommonStockId == commonStock.Id);
    }

    public IQueryable<GovernmentContract> GetByAwardUniqueKey(string awardUniqueKey)
    {
        return GetAll().Where(c => c.AwardUniqueKey == awardUniqueKey);
    }
}
