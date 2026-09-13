using Equibles.Data;
using Equibles.Yahoo.Data.Models;

namespace Equibles.Yahoo.Repositories;

// Unresolved observations are explicit research history, never a substitute for listed prices.
public class UnattributedDailyStockPriceRepository : BaseRepository<UnattributedDailyStockPrice>
{
    public UnattributedDailyStockPriceRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<UnattributedDailyStockPrice> GetByIssuer(Guid issuerId) =>
        GetAll().Where(price => price.EquityIssuerId == issuerId);
}
