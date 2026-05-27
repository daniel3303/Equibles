using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class AumQuarterlySnapshotRepository : BaseRepository<AumQuarterlySnapshot>
{
    public AumQuarterlySnapshotRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
