using Equibles.Data;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.EquityMarkets.Repositories;

public class EquityMarketRegistrationRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<EquityMarketRegistration>(dbContext)
{
    public Task<EquityMarketRegistration> GetByCode(
        string code,
        CancellationToken cancellationToken = default
    ) => GetAll().SingleOrDefaultAsync(row => row.Code == code, cancellationToken);

    // Every catalog market gets a row so its passes have somewhere to land before the first one runs.
    public async Task EnsureSeeded(
        IEnumerable<EquityMarket> markets,
        CancellationToken cancellationToken = default
    )
    {
        var existing = await GetAll().Select(row => row.Code).ToListAsync(cancellationToken);
        var missing = markets
            .Select(market => market.Code)
            .Where(code => !existing.Contains(code))
            .ToList();
        if (missing.Count == 0)
            return;
        foreach (var code in missing)
            Add(new EquityMarketRegistration { Code = code, UpdatedAt = DateTime.UtcNow });
        try
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two workers seeding at once: the loser's rows already exist, nothing is lost.
            DbContext.ChangeTracker.Clear();
        }
    }
}
