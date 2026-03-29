using Equibles.Cftc.Data.Models;
using Equibles.Data;

namespace Equibles.Cftc.Repositories;

public class CftcContractRepository : BaseRepository<CftcContract> {
    public CftcContractRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<CftcContract> GetByMarketCode(string marketCode) {
        return GetAll().Where(c => c.MarketCode == marketCode);
    }

    public IQueryable<CftcContract> GetByCategory(CftcContractCategory category) {
        return GetAll().Where(c => c.Category == category);
    }

    public IQueryable<CftcContract> Search(string query) {
        var lower = query.ToLower();
        return GetAll().Where(c =>
            c.MarketCode.ToLower().Contains(lower) ||
            c.MarketName.ToLower().Contains(lower));
    }
}
