using Equibles.Congress.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Repositories;

public class CongressMemberRepository : BaseRepository<CongressMember>
{
    public CongressMemberRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<CongressMember> GetByName(string name)
    {
        return await GetAll().FirstOrDefaultAsync(m => m.Name == name);
    }

    public IQueryable<CongressMember> Search(string search)
    {
        return GetAll().Where(m => EF.Functions.ILike(m.Name, $"%{search}%"));
    }
}
