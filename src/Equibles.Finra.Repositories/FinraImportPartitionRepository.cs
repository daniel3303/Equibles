using Equibles.Data;
using Equibles.Finra.Data.Models;

namespace Equibles.Finra.Repositories;

public class FinraImportPartitionRepository : BaseRepository<FinraImportPartition>
{
    public FinraImportPartitionRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FinraImportPartition> GetRange(
        string dataset,
        string scopeKey,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetAll()
            .Where(p =>
                p.Dataset == dataset
                && p.ScopeKey == scopeKey
                && p.PartitionDate >= startDate
                && p.PartitionDate <= endDate
            );
    }

    public IQueryable<FinraImportPartition> GetPartition(
        string dataset,
        string scopeKey,
        DateOnly partitionDate
    )
    {
        return GetAll()
            .Where(p =>
                p.Dataset == dataset && p.ScopeKey == scopeKey && p.PartitionDate == partitionDate
            );
    }
}
