using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.GovernmentContracts.Data.Models;

namespace Equibles.GovernmentContracts.Repositories;

public class GovernmentContractRepository : BaseRepository<GovernmentContract>
{
    public GovernmentContractRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<GovernmentContract> GetByIssuerId(Guid commonStock)
    {
        return GetAll().Where(c => c.EquityIssuerId == commonStock);
    }

    public IQueryable<GovernmentContract> GetByAwardUniqueKey(string awardUniqueKey)
    {
        return GetAll().Where(c => c.AwardUniqueKey == awardUniqueKey);
    }
}
