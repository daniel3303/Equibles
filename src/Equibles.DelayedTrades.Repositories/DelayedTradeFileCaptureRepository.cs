using Equibles.Data;
using Equibles.DelayedTrades.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.DelayedTrades.Repositories;

public class DelayedTradeFileCaptureRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<DelayedTradeFileCapture>(dbContext)
{
    public IQueryable<DelayedTradeFileCapture> GetByMarket(string marketCode) =>
        GetAll().Where(row => row.MarketCode == marketCode);

    public async Task<int> PruneBefore(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default
    )
    {
        var stale = GetAll().Where(row => row.FetchedAtUtc < cutoffUtc);
        if (DbContext.Database.IsRelational())
            return await stale.ExecuteDeleteAsync(cancellationToken);
        var rows = await stale.ToListAsync(cancellationToken);
        Delete(rows);
        await DbContext.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
