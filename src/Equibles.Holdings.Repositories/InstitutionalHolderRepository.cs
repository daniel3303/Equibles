using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class InstitutionalHolderRepository : BaseRepository<InstitutionalHolder> {
    public InstitutionalHolderRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public async Task<InstitutionalHolder> GetByCik(string cik) {
        return await GetAll().FirstOrDefaultAsync(h => h.Cik == cik);
    }

    public IQueryable<InstitutionalHolder> GetByCiks(IEnumerable<string> ciks) {
        return GetAll().Where(h => ciks.Contains(h.Cik));
    }

    public IQueryable<InstitutionalHolder> Search(string search) {
        return GetAll().Where(h => EF.Functions.ILike(h.Name, $"%{search}%"));
    }
}
