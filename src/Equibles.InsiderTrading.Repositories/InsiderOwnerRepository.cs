using Equibles.Data;
using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Repositories;

public class InsiderOwnerRepository : BaseRepository<InsiderOwner>
{
    public InsiderOwnerRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<InsiderOwner> GetByOwnerCik(string ownerCik)
    {
        return await GetAll().FirstOrDefaultAsync(o => o.OwnerCik == ownerCik);
    }

    public IQueryable<InsiderOwner> GetByOwnerCiks(IEnumerable<string> ownerCiks)
    {
        return GetAll().Where(o => ownerCiks.Contains(o.OwnerCik));
    }

    public IQueryable<InsiderOwner> Search(string search)
    {
        var tokens = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var query = GetAll();
        foreach (var token in tokens)
        {
            var t = token;
            query = query.Where(o => EF.Functions.ILike(o.Name, $"%{t}%"));
        }

        return query;
    }
}
