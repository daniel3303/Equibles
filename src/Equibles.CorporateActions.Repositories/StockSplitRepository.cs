using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Repositories;

public class StockSplitRepository : BaseRepository<StockSplit>
{
    public StockSplitRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<StockSplit> GetByStock(Guid commonStockId)
    {
        return GetAll().Where(s => s.CommonStockId == commonStockId);
    }

    public IQueryable<StockSplit> GetPendingPriceAdjustment()
    {
        return GetAll().Where(s => s.PriceAdjustmentAppliedTime == null);
    }

    /// <summary>
    /// Loads and locks the selected split rows until the caller's current transaction completes.
    /// This serializes reconciliation stamping with a retiring worker that does not lock the
    /// parent stock before revising an already-selected split.
    /// </summary>
    public async Task<List<StockSplit>> GetForUpdate(
        IEnumerable<Guid> stockSplitIds,
        CancellationToken cancellationToken = default
    )
    {
        var ids = stockSplitIds.Distinct().ToArray();
        if (ids.Length == 0)
            return [];

        if (!DbContext.Database.IsRelational())
        {
            return await GetAll()
                .Where(split => ids.Contains(split.Id))
                .ToListAsync(cancellationToken);
        }

        if (DbContext.Database.CurrentTransaction == null)
            throw new InvalidOperationException("GetForUpdate requires an active transaction.");

        return await GetDbSet()
            .FromSqlInterpolated(
                $"""SELECT * FROM "StockSplit" WHERE "Id" = ANY({ids}) FOR UPDATE"""
            )
            .ToListAsync(cancellationToken);
    }
}
