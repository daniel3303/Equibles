using Equibles.Data;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.Repositories;

public class FileRepository : BaseRepository<File> {

    public FileRepository(EquiblesDbContext dbContext) : base(dbContext) {

    }
}