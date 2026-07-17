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
        // Case-insensitive exact match (same pattern as CommonStockRepository.GetByName):
        // the name arrives as caller-typed input, and == is case-sensitive in PostgreSQL,
        // so 'nancy pelosi' would miss 'Nancy Pelosi'.
        return await GetAll().FirstOrDefaultAsync(m => m.Name.ToLower() == name.ToLower());
    }

    public IQueryable<CongressMember> Search(string search)
    {
        // "Name contains the term"; escape so '%' / '_' / '\' in the query match literally.
        var pattern = LikePattern.Contains(search);
        return GetAll().Where(m => EF.Functions.ILike(m.Name, pattern, LikePattern.EscapeChar));
    }
}
