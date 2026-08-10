using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Repositories;

public class CorporateActionPriceReconciliationCursorRepository
    : BaseRepository<CorporateActionPriceReconciliationCursor>
{
    public CorporateActionPriceReconciliationCursorRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// Loads and locks the singleton cursor until the caller's transaction completes.
    /// </summary>
    public async Task<CorporateActionPriceReconciliationCursor> GetForUpdate(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        if (!DbContext.Database.IsRelational())
            return await GetAll().SingleOrDefaultAsync(row => row.Name == name, cancellationToken);

        if (DbContext.Database.CurrentTransaction == null)
            throw new InvalidOperationException("GetForUpdate requires an active transaction.");

        return await GetDbSet()
            .FromSqlInterpolated(
                $"""SELECT * FROM "CorporateActionPriceReconciliationCursor" WHERE "Name" = {name} FOR UPDATE"""
            )
            .SingleOrDefaultAsync(cancellationToken);
    }
}
