using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class RawFilingArtifactRepository : BaseRepository<RawFilingArtifact>
{
    public RawFilingArtifactRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<RawFilingArtifact> GetByStock(CommonStock stock)
    {
        return GetAll().Where(a => a.CommonStockId == stock.Id);
    }

    public IQueryable<RawFilingArtifact> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(a => a.AccessionNumber == accessionNumber);
    }

    public Task<bool> Exists(string accessionNumber, RawFilingArtifactType artifactType)
    {
        return GetAll()
            .AnyAsync(a => a.AccessionNumber == accessionNumber && a.ArtifactType == artifactType);
    }
}
