using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class IsinLegalEntityLookupRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<IsinLegalEntityLookup>(dbContext)
{
    /// <summary>
    /// ISINs of actively listed, verified securities whose issuer has no LEI, never asked or due again;
    /// the longest-waiting first.
    /// </summary>
    public Task<List<string>> GetDue(DateTime now, int take, CancellationToken cancellationToken) =>
        DbContext
            .Set<EquitySecurity>()
            .Where(security =>
                security.Isin != null
                && security.Issuer.LegalEntityIdentifier == null
                && security.Listings.Any(listing =>
                    listing.Active && listing.IdentityState == EquityIdentityState.Verified
                )
            )
            .Select(security => security.Isin)
            .Distinct()
            .Where(isin => !GetAll().Any(row => row.Isin == isin && row.NextAttemptAt > now))
            .OrderBy(isin =>
                GetAll()
                    .Where(row => row.Isin == isin)
                    .Select(row => (DateTime?)row.NextAttemptAt)
                    .FirstOrDefault() ?? DateTime.MinValue
            )
            .ThenBy(isin => isin)
            .Take(take)
            .ToListAsync(cancellationToken);

    public IQueryable<IsinLegalEntityLookup> GetByIsins(IEnumerable<string> isins) =>
        GetAll().Where(row => isins.Contains(row.Isin));
}
