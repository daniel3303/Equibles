using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class EquityIssuerRepository : BaseRepository<EquityIssuer>
{
    public EquityIssuerRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<EquityIssuer> GetByLegacyStock(Guid stockId) =>
        GetAll().Where(row => row.CommonStockId == stockId);

    // Financial statements and other issuer facts retain their original IDs and tables.
    public IQueryable<CommonStock> GetLegacyFacts(Guid issuerId) =>
        GetAll()
            .Where(row => row.Id == issuerId && row.CommonStockId != null)
            .Select(row => row.CommonStock);
}
