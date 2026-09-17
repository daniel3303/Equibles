using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class EsefOversizedReportRepository : BaseRepository<EsefOversizedReport>
{
    public EsefOversizedReportRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// Every refusal on record with the ceiling it was made under, so one read serves both the skip (a
    /// refusal at or above the caller's own ceiling) and the cleanup of a row whose report has since been
    /// stored under a higher one.
    /// </summary>
    public IQueryable<EsefRefusalRecord> GetRefusals() =>
        GetAll().Select(row => new EsefRefusalRecord(row.Reference, row.CeilingBytes));
}

public record EsefRefusalRecord(string Reference, int CeilingBytes);
