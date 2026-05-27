using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class InstitutionalHolderRepository : BaseRepository<InstitutionalHolder>
{
    public InstitutionalHolderRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<InstitutionalHolder> GetByCik(string cik)
    {
        return await GetAll().FirstOrDefaultAsync(h => h.Cik == cik);
    }

    public IQueryable<InstitutionalHolder> GetByCiks(IEnumerable<string> ciks)
    {
        return GetAll().Where(h => ciks.Contains(h.Cik));
    }

    public IQueryable<InstitutionalHolder> Search(string search)
    {
        return GetAll().Where(h => EF.Functions.ILike(h.Name, $"%{search}%"));
    }

    // Typeahead variant: matches a CIK prefix as well as a name substring so the
    // picker can resolve either "berk" or "0001067" to the same row. The user's
    // input is escaped first so '%' / '_' / '\\' in the query don't behave as LIKE
    // wildcards (e.g. "50%" would otherwise match every name).
    public IQueryable<InstitutionalHolder> SearchNameOrCik(string search)
    {
        var escaped = EscapeLikePattern(search);
        var namePattern = $"%{escaped}%";
        var cikPrefix = $"{escaped}%";
        return GetAll()
            .Where(h =>
                EF.Functions.ILike(h.Name, namePattern, "\\")
                || EF.Functions.ILike(h.Cik, cikPrefix, "\\")
            );
    }

    private static string EscapeLikePattern(string input) =>
        input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public IQueryable<InstitutionalHolder> GetUnclassified()
    {
        return GetAll()
            .Where(h => h.Classification == FundClassification.Unknown && h.Name != null);
    }
}
