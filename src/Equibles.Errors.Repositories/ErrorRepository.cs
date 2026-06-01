using Equibles.Data;
using Equibles.Errors.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Errors.Repositories;

public class ErrorRepository : BaseRepository<Error>
{
    public ErrorRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<Error> GetUnseen()
    {
        return GetAll().Where(e => !e.Seen);
    }

    public IQueryable<Error> Search(string search)
    {
        if (string.IsNullOrEmpty(search))
            return GetAll();
        // Escape LIKE metacharacters so '%' / '_' / '\' in the query match literally rather
        // than as wildcards (a bare '_' would otherwise match every row, '%' the whole table),
        // matching the "Context or Message contains the term" contract and the escaping used
        // elsewhere.
        var pattern = $"%{search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        return GetAll()
            .Where(e =>
                EF.Functions.ILike(e.Context, pattern, "\\")
                || EF.Functions.ILike(e.Message, pattern, "\\")
            );
    }

    public IQueryable<Error> GetBySource(ErrorSource source)
    {
        return GetAll().Where(e => e.Source == source);
    }
}
