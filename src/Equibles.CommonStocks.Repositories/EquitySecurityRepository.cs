using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class EquitySecurityRepository : BaseRepository<EquitySecurity>
{
    public EquitySecurityRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// Moves a security's market cap only while its stored share count and cap are still the pair
    /// the new figure was computed from; false when another writer replaced the pair meanwhile.
    /// The pair's other writers hold the issuer lock, which this single statement does not need.
    /// </summary>
    public async Task<bool> ReplaceMarketCapitalization(
        Guid securityId,
        long sharesOutstanding,
        double marketCapitalization,
        double replacement,
        CancellationToken cancellationToken = default
    )
    {
        var current = GetAll()
            .Where(security =>
                security.Id == securityId
                && security.SharesOutstanding == sharesOutstanding
                && security.MarketCapitalization == marketCapitalization
            );
        if (DbContext.Database.IsRelational())
            return await current.ExecuteUpdateAsync(
                    setters =>
                        setters.SetProperty(security => security.MarketCapitalization, replacement),
                    cancellationToken
                ) == 1;
        var security = await current.SingleOrDefaultAsync(cancellationToken);
        if (security == null)
            return false;
        security.MarketCapitalization = replacement;
        await DbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
