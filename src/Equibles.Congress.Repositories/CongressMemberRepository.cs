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
        // Escape LIKE metacharacters so '%' / '_' / '\' in the query match literally rather
        // than as wildcards (a bare '_' would otherwise match every name, '%' the whole table),
        // matching the "name contains the term" contract and the escaping used elsewhere.
        var pattern = $"%{search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        return GetAll().Where(m => EF.Functions.ILike(m.Name, pattern, "\\"));
    }
}
