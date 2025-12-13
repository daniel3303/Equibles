using Equibles.Data;
using Equibles.Media.Data.Models;

namespace Equibles.Media.Repositories;

public class ImageRepository : BaseRepository<Image> {

    public ImageRepository(EquiblesDbContext dbContext) : base(dbContext) {

    }
}