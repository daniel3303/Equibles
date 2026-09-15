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

    public IQueryable<EquityMarketRegistration> GetEnabled() => GetAll().Where(row => row.Enabled);

    public Task<List<string>> GetEnabledCodes(CancellationToken cancellationToken = default) =>
        GetEnabled().Select(row => row.Code).ToListAsync(cancellationToken);

    // Every catalog market gets a row so the operator page can toggle it; a code listed in
    // initiallyEnabled is created switched on (the one-time seed for a lane that was already live).
    public async Task EnsureSeeded(
        IEnumerable<EquityMarket> markets,
        IReadOnlySet<string> initiallyEnabled,
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
            Add(
                new EquityMarketRegistration
                {
                    Code = code,
                    Enabled = initiallyEnabled.Contains(code),
                    UpdatedAt = DateTime.UtcNow,
                }
            );
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
