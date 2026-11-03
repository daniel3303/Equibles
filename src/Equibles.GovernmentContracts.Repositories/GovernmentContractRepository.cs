using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.GovernmentContracts.Data.Models;

namespace Equibles.GovernmentContracts.Repositories;

public class GovernmentContractRepository : BaseRepository<GovernmentContract>
{
    public GovernmentContractRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    // Every reader names the awarding country: amounts are in that country's currency, so one
    // query never sums US and Canadian dollars together.
    public IQueryable<GovernmentContract> GetByIssuerId(Guid issuerId, string countryCode)
    {
        return GetByCountry(countryCode).Where(c => c.EquityIssuerId == issuerId);
    }

    public IQueryable<GovernmentContract> GetByCountry(string countryCode)
    {
        return GetAll().Where(c => c.CountryCode == countryCode);
    }

    public IQueryable<GovernmentContract> GetByAwardUniqueKey(string awardUniqueKey)
    {
        return GetAll().Where(c => c.AwardUniqueKey == awardUniqueKey);
    }
}
