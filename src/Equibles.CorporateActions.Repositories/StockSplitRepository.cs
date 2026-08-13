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

    /// <summary>
    /// The stock's splits already effective as of <paramref name="asOf"/> — the set every
    /// read-time restatement must use. Split rows are captured at announcement, ahead of their
    /// effective date, so restating a historical series with the unfiltered set scales it by a
    /// split that has not happened yet, for the whole announcement window. <see cref="GetByStock"/>
    /// remains for writers (capture/reconciliation), which do need the announced rows.
    /// </summary>
    public IQueryable<StockSplit> GetEffectiveByStock(Guid commonStockId, DateOnly asOf)
    {
        return GetByStock(commonStockId).Where(s => s.EffectiveDate <= asOf);
    }

    /// <summary>
    /// Batch companion to <see cref="GetEffectiveByStock"/> for callers that restate many
    /// stocks with one query: every split already effective as of <paramref name="asOf"/>,
    /// across all stocks. Compose the stock filter on top.
    /// </summary>
    public IQueryable<StockSplit> GetEffective(DateOnly asOf)
    {
        return GetAll().Where(s => s.EffectiveDate <= asOf);
    }

    public IQueryable<StockSplit> GetPendingPriceAdjustment()
    {
        // SQL-translatable mirror of StockSplit.IsPriceAdjustmentApplied: a marker written on or
        // before the effective date came from the old worker, which could stamp a future split.
        return GetAll()
            .Where(split =>
                split.PriceAdjustmentAppliedTime == null
                || DateOnly.FromDateTime(split.PriceAdjustmentAppliedTime.Value)
                    <= split.EffectiveDate
            );
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
