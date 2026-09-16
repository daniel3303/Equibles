using Equibles.Data;
using Equibles.DelayedTrades.Data.Models;
using Equibles.Integrations.DelayedTrades;
using Microsoft.EntityFrameworkCore;

namespace Equibles.DelayedTrades.Repositories;

public class DelayedTradeFileCaptureRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<DelayedTradeFileCapture>(dbContext)
{
    public IQueryable<DelayedTradeFileCapture> GetByMarket(string marketCode) =>
        GetAll().Where(row => row.MarketCode == marketCode);

    // Whether an intraday poll saw prints of the session: the evidence that a session existed and the venue's
    // previous-session file has simply not flipped to it yet.
    public Task<bool> HasSessionCapture(
        string marketCode,
        DateOnly sessionDate,
        CancellationToken cancellationToken = default
    ) =>
        GetByMarket(marketCode)
            .AnyAsync(
                row =>
                    row.Window == DelayedTradeWindow.CurrentSession
                    && row.Outcome == DelayedTradeFetchOutcome.Served
                    && row.SessionDate == sessionDate,
                cancellationToken
            );

    public Task<int?> GetRowsOfFile(
        string marketCode,
        string sha256,
        CancellationToken cancellationToken = default
    ) =>
        GetByMarket(marketCode)
            .Where(row => row.Sha256 == sha256 && row.Rows > 0)
            .OrderByDescending(row => row.FetchedAtUtc)
            .Select(row => (int?)row.Rows)
            .FirstOrDefaultAsync(cancellationToken);

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
