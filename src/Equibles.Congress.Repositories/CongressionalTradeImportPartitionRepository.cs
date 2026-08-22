using Equibles.Congress.Data.Models;
using Equibles.Data;

namespace Equibles.Congress.Repositories;

public class CongressionalTradeImportPartitionRepository
    : BaseRepository<CongressionalTradeImportPartition>
{
    public CongressionalTradeImportPartitionRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CongressionalTradeImportPartition> GetByKind(CongressionalFilingKind kind) =>
        GetAll().Where(p => p.Kind == kind);
}
