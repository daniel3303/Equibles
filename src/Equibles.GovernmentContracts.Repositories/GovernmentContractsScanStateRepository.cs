using Equibles.Data;
using Equibles.GovernmentContracts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.GovernmentContracts.Repositories;

public class GovernmentContractsScanStateRepository : BaseRepository<GovernmentContractsScanState>
{
    public GovernmentContractsScanStateRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public virtual async Task<GovernmentContractsScanState> GetByName(string name)
    {
        return await GetAll().FirstOrDefaultAsync(s => s.Name == name);
    }
}
