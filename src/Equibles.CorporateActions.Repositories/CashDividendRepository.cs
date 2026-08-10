using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Repositories;

public class CashDividendRepository : BaseRepository<CashDividend>
{
    public CashDividendRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CashDividend> GetByStock(Guid commonStockId)
    {
        return GetAll().Where(d => d.CommonStockId == commonStockId);
    }

    public IQueryable<CashDividend> GetPendingPriceAdjustment()
    {
        return GetAll()
            .Where(d =>
                d.PriceAdjustmentAppliedTime == null
                || d.PriceAdjustmentAppliedAmountPerShare == null
                || d.PriceAdjustmentAppliedAmountPerShare != d.AmountPerShare
            );
    }

    /// <summary>
    /// Loads and locks the selected dividend rows until the caller's transaction completes.
    /// </summary>
    public async Task<List<CashDividend>> GetForUpdate(
        IEnumerable<Guid> cashDividendIds,
        CancellationToken cancellationToken = default
    )
    {
        var ids = cashDividendIds.Distinct().ToArray();
        if (ids.Length == 0)
            return [];

        if (!DbContext.Database.IsRelational())
        {
            return await GetAll()
                .Where(dividend => ids.Contains(dividend.Id))
                .ToListAsync(cancellationToken);
        }

        if (DbContext.Database.CurrentTransaction == null)
            throw new InvalidOperationException("GetForUpdate requires an active transaction.");

        return await GetDbSet()
            .FromSqlInterpolated(
                $"""SELECT * FROM "CashDividend" WHERE "Id" = ANY({ids}) FOR UPDATE"""
            )
            .ToListAsync(cancellationToken);
    }
}
