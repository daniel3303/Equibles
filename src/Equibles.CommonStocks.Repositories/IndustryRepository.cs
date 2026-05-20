using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class IndustryRepository : BaseRepository<Industry>
{
    public IndustryRepository(EquiblesDbContext dbContext)
        : base(dbContext) { }
}
