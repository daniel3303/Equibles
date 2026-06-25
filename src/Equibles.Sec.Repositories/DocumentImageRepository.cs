using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class DocumentImageRepository : BaseRepository<DocumentImage>
{
    public DocumentImageRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
