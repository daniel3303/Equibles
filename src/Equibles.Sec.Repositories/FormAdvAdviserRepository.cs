using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class FormAdvAdviserRepository : BaseRepository<FormAdvAdviser>
{
    public FormAdvAdviserRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FormAdvAdviser> GetByCrd(int crd)
    {
        return GetAll().Where(a => a.Crd == crd);
    }

    /// <summary>
    /// Matches advisers whose legal or primary business name contains <paramref name="term"/>,
    /// largest by total regulatory assets under management first. Returns nothing for a blank term.
    /// </summary>
    public IQueryable<FormAdvAdviser> Search(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return GetAll().Where(a => false);
        }

        var trimmed = term.Trim();
        // Escape LIKE metacharacters so "_" and "%" in the query match literally
        // rather than acting as wildcards; pair with an explicit ESCAPE clause.
        var escaped = trimmed.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var pattern = $"%{escaped}%";
        return GetAll()
            .Where(a =>
                EF.Functions.ILike(a.LegalName, pattern, "\\")
                || EF.Functions.ILike(a.PrimaryBusinessName, pattern, "\\")
            )
            // Coalesce so advisers that did not report assets sort last rather than first
            // (Postgres orders NULL highest under a plain DESC).
            .OrderByDescending(a => a.TotalRegulatoryAum ?? 0L);
    }

    /// <summary>Advisers ordered by total regulatory assets under management, largest first.</summary>
    public IQueryable<FormAdvAdviser> GetLargestByAum()
    {
        return GetAll().OrderByDescending(a => a.TotalRegulatoryAum ?? 0L);
    }
}
