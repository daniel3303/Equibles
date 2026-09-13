using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class EquitySecurityRepository : BaseRepository<EquitySecurity>
{
    public EquitySecurityRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
