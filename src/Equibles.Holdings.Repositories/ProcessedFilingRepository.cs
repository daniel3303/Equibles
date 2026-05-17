using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class ProcessedFilingRepository : BaseRepository<ProcessedFiling>
{
    public ProcessedFilingRepository(EquiblesDbContext dbContext)
        : base(dbContext) { }

    public async Task<bool> Exists(string accessionNumber)
    {
        return await GetAll().AnyAsync(p => p.AccessionNumber == accessionNumber);
    }

    public IQueryable<ProcessedFiling> GetByAccessionNumbers(IEnumerable<string> accessionNumbers)
    {
        return GetAll().Where(p => accessionNumbers.Contains(p.AccessionNumber));
    }
}
