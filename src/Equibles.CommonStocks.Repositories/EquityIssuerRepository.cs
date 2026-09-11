using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class EquityIssuerRepository : BaseRepository<EquityIssuer>
{
    public EquityIssuerRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
