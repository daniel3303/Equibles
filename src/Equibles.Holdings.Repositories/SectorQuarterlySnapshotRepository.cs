using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class SectorQuarterlySnapshotRepository : BaseRepository<SectorQuarterlySnapshot>
{
    public SectorQuarterlySnapshotRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
