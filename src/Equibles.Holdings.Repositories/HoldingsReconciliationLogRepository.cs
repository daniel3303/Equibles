using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class HoldingsReconciliationLogRepository : BaseRepository<HoldingsReconciliationLog>
{
    public HoldingsReconciliationLogRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    // Most recent runs first — backs the Backoffice reconciliation log list.
    public IQueryable<HoldingsReconciliationLog> GetRecentFirst() =>
        GetAll().OrderByDescending(l => l.CreationTime);

    // Filers reconciled at or after <paramref name="since"/>. The "reconcile next
    // lagging filer" cursor skips these so repeated clicks advance the backlog
    // instead of re-checking the largest laggard every time.
    public IQueryable<Guid> GetHolderIdsCheckedSince(DateTime since) =>
        GetAll().Where(l => l.CreationTime >= since).Select(l => l.InstitutionalHolderId);
}
