using Equibles.Data;
using Equibles.Errors.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Errors.Repositories;

public class WorkerCycleRepository : BaseRepository<WorkerCycle>
{
    public WorkerCycleRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public Task<DateTimeOffset?> GetCompletedAt(
        string workerName,
        CancellationToken cancellationToken = default
    ) =>
        GetAll()
            .Where(cycle => cycle.WorkerName == workerName)
            .Select(cycle => (DateTimeOffset?)cycle.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task MarkCompleted(
        string workerName,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default
    )
    {
        var cycle = await GetAll()
            .FirstOrDefaultAsync(c => c.WorkerName == workerName, cancellationToken);
        if (cycle == null)
        {
            cycle = new WorkerCycle { WorkerName = workerName };
            Add(cycle);
        }
        cycle.CompletedAt = completedAt;
        await DbContext.SaveChangesAsync(cancellationToken);
    }
}
