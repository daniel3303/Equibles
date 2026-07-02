using Equibles.Data;
using Equibles.Media.Data.Models;

namespace Equibles.Media.Repositories;

public class PendingBlobDeletionRepository : BaseRepository<PendingBlobDeletion>
{
    public PendingBlobDeletionRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
