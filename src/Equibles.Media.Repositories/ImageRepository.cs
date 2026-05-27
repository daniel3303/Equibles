using Equibles.Data;
using Equibles.Media.Data.Models;

namespace Equibles.Media.Repositories;

public class ImageRepository : BaseRepository<Image>
{
    public ImageRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
