using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class ProcessedDataSetRepository : BaseRepository<ProcessedDataSet>
{
    public ProcessedDataSetRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<ProcessedDataSet> GetByFileName(string fileName)
    {
        return GetAll().Where(p => p.FileName == fileName);
    }
}
