using Equibles.Data;
using Equibles.Sec.Data.Contracts;

namespace Equibles.Sec.Repositories;

/// <summary>
/// Base repository for issuer-attributed SEC filings, providing the by-issuer,
/// by-accession-number and recent queries shared by every filing type.
/// </summary>
public abstract class SecFilingRepositoryBase<TFiling> : BaseRepository<TFiling>
    where TFiling : class, IIssuerFiling
{
    protected SecFilingRepositoryBase(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<TFiling> GetByIssuerId(Guid issuerId)
    {
        return GetAll().Where(f => f.EquityIssuerId == issuerId);
    }

    public IQueryable<TFiling> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(f => f.AccessionNumber == accessionNumber);
    }

    public IQueryable<TFiling> GetRecent(DateOnly since)
    {
        return GetAll().Where(f => f.FilingDate >= since);
    }
}
