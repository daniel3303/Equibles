using Equibles.Congress.Data.Models;
using Equibles.Data;

namespace Equibles.Congress.Repositories;

public class CongressionalFilingRecordRepository : BaseRepository<CongressionalFilingRecord>
{
    public CongressionalFilingRecordRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CongressionalFilingRecord> GetByKind(CongressionalFilingKind kind)
    {
        return GetAll().Where(r => r.Kind == kind);
    }
}
