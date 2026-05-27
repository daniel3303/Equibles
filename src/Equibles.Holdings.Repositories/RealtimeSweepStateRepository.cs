using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class RealtimeSweepStateRepository : BaseRepository<RealtimeSweepState>
{
    public RealtimeSweepStateRepository(EquiblesDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<RealtimeSweepState> GetByWorker(string workerName)
    {
        return GetAll().Where(s => s.WorkerName == workerName);
    }
}
