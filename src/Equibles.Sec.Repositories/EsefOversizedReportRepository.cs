using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class EsefOversizedReportRepository : BaseRepository<EsefOversizedReport>
{
    public EsefOversizedReportRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// The references refused at or above the caller's own ceiling, which are the ones it must not fetch
    /// again. A report refused under a lower ceiling is left out, so raising the ceiling re-opens it.
    /// </summary>
    public IQueryable<string> GetReferencesRefusedAtOrAbove(int ceilingBytes) =>
        GetAll().Where(row => row.CeilingBytes >= ceilingBytes).Select(row => row.Reference);
}
