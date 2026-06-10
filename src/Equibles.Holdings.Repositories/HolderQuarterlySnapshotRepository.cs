using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class HolderQuarterlySnapshotRepository : BaseRepository<HolderQuarterlySnapshot>
{
    public HolderQuarterlySnapshotRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
