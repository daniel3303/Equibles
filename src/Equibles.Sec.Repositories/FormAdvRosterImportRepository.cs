using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class FormAdvRosterImportRepository : BaseRepository<FormAdvRosterImport>
{
    public FormAdvRosterImportRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FormAdvRosterImport> GetByRoster(
        FormAdvRegistration registration,
        DateOnly reportDate
    )
    {
        return GetAll()
            .Where(import =>
                import.Registration == registration && import.ReportDate == reportDate
            );
    }
}
