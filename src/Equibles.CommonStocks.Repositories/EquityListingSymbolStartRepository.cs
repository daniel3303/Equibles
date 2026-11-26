using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Equibles.CommonStocks.Repositories;

public class EquityListingSymbolStartRepository : BaseRepository<EquityListingSymbolStart>
{
    public EquityListingSymbolStartRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<EquityListingSymbolStart> GetBySource(string evidenceSource) =>
        GetAll().Where(start => start.EvidenceSource == evidenceSource);

    /// <summary>
    /// A transaction on the financial context, so recorded starts commit together with the source
    /// rows and import markers a withdrawal or a lift changes.
    /// </summary>
    public Task<IDbContextTransaction> BeginTransaction(CancellationToken cancellationToken) =>
        DbContext.Database.BeginTransactionAsync(cancellationToken);
}
