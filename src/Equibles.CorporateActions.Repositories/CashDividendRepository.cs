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
        return GetAll().Where(d => d.EquityIssuerId == commonStockId);
    }

    public IQueryable<CashDividend> GetByListing(Guid listingId) =>
        GetAll().Where(row => row.EquityListingId == listingId);

    public IQueryable<CashDividend> GetHistoryByListing(
        Guid listingId,
        DateOnly? startDate = null,
        DateOnly? endDate = null
    )
    {
        var query = GetByListing(listingId);
        if (startDate.HasValue)
            query = query.Where(d => d.ExDate >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(d => d.ExDate <= endDate.Value);

        return query.OrderByDescending(d => d.ExDate).ThenByDescending(d => d.Id);
    }

    /// <summary>
    /// Issuer-only rows from captures that predate listing attribution with no attributed row on
    /// the same issuer within <paramref name="toleranceDays"/> of the ex-date: listing-keyed reads
    /// never serve these payments.
    /// </summary>
    public IQueryable<CashDividend> GetUnattributedWithoutListingRow(int toleranceDays)
    {
        var listingRows = GetAll().Where(row => row.EquityListingId != null);
        return GetAll()
            .Where(row => row.EquityListingId == null)
            .Where(row =>
                !listingRows.Any(attributed =>
                    attributed.EquityIssuerId == row.EquityIssuerId
                    && attributed.ExDate >= row.ExDate.AddDays(-toleranceDays)
                    && attributed.ExDate <= row.ExDate.AddDays(toleranceDays)
                )
            );
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
