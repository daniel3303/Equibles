using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class SectorRepository : BaseRepository<Sector>
{
    public SectorRepository(EquiblesDbContext dbContext)
        : base(dbContext) { }
}
