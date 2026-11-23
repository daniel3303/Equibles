using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class IsinShareClassLookupRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<IsinShareClassLookup>(dbContext)
{
    /// <summary>
    /// ISINs of actively listed securities whose issuer holds no CIK, never asked or due again; the
    /// longest-waiting first. Only such an issuer can be the venue half of a company that also trades
    /// in the US under an SEC registration.
    /// </summary>
    public Task<List<string>> GetDue(DateTime now, int take, CancellationToken cancellationToken) =>
        DbContext
            .Set<EquitySecurity>()
            .Where(security =>
                security.Isin != null
                && security.Issuer.Cik == null
                && security.Listings.Any(listing => listing.Active)
            )
            .Select(security => security.Isin)
            .Distinct()
            .Where(isin => !GetAll().Any(row => row.Isin == isin && row.NextAttemptAt > now))
            .OrderBy(isin =>
                GetAll()
                    .Where(row => row.Isin == isin)
                    .Select(row => (DateTime?)row.NextAttemptAt)
                    .FirstOrDefault()
                ?? DateTime.MinValue
            )
            .ThenBy(isin => isin)
            .Take(take)
            .ToListAsync(cancellationToken);

    public IQueryable<IsinShareClassLookup> GetByIsins(IEnumerable<string> isins) =>
        GetAll().Where(row => isins.Contains(row.Isin));
}
