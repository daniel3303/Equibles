using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class EquityIssuerRepository : BaseRepository<EquityIssuer>
{
    public EquityIssuerRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<EquityIssuer> GetByLegacyStock(Guid stockId) =>
        GetAll().Where(row => row.CommonStockId == stockId);

    public IQueryable<EquityIssuer> GetByCik(string cik) => GetAll().Where(row => row.Cik == cik);
}
