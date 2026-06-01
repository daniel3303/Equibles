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
        // "Context or Message contains the term"; escape so '%' / '_' / '\' match literally.
        var pattern = LikePattern.Contains(search);
        return GetAll()
            .Where(e =>
                EF.Functions.ILike(e.Context, pattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(e.Message, pattern, LikePattern.EscapeChar)
            );
    }

    public IQueryable<Error> GetBySource(ErrorSource source)
    {
        return GetAll().Where(e => e.Source == source);
    }
}
