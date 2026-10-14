using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class EquityIssuerMergeRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<EquityIssuerMerge>(dbContext)
{
    public Task<EquityIssuerMerge> GetByRetired(
        Guid retiredIssuerId,
        CancellationToken cancellationToken = default
    ) =>
        GetAll()
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.RetiredIssuerId == retiredIssuerId, cancellationToken);

    public IQueryable<EquityIssuerMerge> GetPendingCustomerRepoint() =>
        GetAll().Where(row => row.CustomerReferencesRepointedAt == null);

    public Task<int> MarkCustomerReferencesRepointed(
        Guid retiredIssuerId,
        DateTime repointedAt,
        CancellationToken cancellationToken = default
    ) =>
        GetAll()
            .Where(row =>
                row.RetiredIssuerId == retiredIssuerId && row.CustomerReferencesRepointedAt == null
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.CustomerReferencesRepointedAt, repointedAt),
                cancellationToken
            );
}
