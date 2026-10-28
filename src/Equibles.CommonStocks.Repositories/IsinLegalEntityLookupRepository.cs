using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class IsinLegalEntityLookupRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<IsinLegalEntityLookup>(dbContext)
{
    /// <summary>
    /// ISINs of actively listed, verified securities never asked or due again, with the LEI their issuer
    /// holds; the longest-waiting first. A row answered before its issuer held an LEI is due at once, so
    /// the LEI's own ISINs replace an answer GLEIF could not give by ISIN.
    /// </summary>
    public async Task<List<IsinLegalEntityLookupDue>> GetDue(
        DateTime now,
        int take,
        CancellationToken cancellationToken
    )
    {
        var due = await DbContext
            .Set<EquitySecurity>()
            .Where(security =>
                security.Isin != null
                && security.Listings.Any(listing =>
                    listing.Active && listing.IdentityState == EquityIdentityState.Verified
                )
            )
            .Select(security => new { security.Isin, Lei = security.Issuer.LegalEntityIdentifier })
            .Distinct()
            .Where(due =>
                !GetAll()
                    .Any(row =>
                        row.Isin == due.Isin
                        && row.NextAttemptAt > now
                        && (
                            due.Lei == null
                            || row.LegalEntityIdentifier == due.Lei
                            || row.LastError != null
                        )
                    )
            )
            .OrderBy(due =>
                GetAll()
                    .Where(row => row.Isin == due.Isin)
                    .Select(row => (DateTime?)row.NextAttemptAt)
                    .FirstOrDefault()
                ?? DateTime.MinValue
            )
            .ThenBy(due => due.Isin)
            .Take(take)
            .ToListAsync(cancellationToken);
        return due.Select(row => new IsinLegalEntityLookupDue(row.Isin, row.Lei)).ToList();
    }

    public IQueryable<IsinLegalEntityLookup> GetByIsins(IEnumerable<string> isins) =>
        GetAll().Where(row => isins.Contains(row.Isin));
}
