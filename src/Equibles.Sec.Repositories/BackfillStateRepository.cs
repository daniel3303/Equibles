using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class BackfillStateRepository : BaseRepository<BackfillState>
{
    public BackfillStateRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public virtual async Task<BackfillState> GetByName(string name)
    {
        return await GetAll().FirstOrDefaultAsync(s => s.Name == name);
    }
}
