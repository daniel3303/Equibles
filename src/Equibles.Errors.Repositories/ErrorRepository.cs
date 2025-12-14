using Equibles.Data;
using Equibles.Errors.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Errors.Repositories;

public class ErrorRepository : BaseRepository<Error> {
    public ErrorRepository(EquiblesDbContext dbContext) : base(dbContext) { }

    public IQueryable<Error> GetUnseen() {
        return GetAll().Where(e => !e.Seen);
    }

    public IQueryable<Error> Search(string search) {
        if (string.IsNullOrEmpty(search)) return GetAll();
        return GetAll().Where(e => EF.Functions.ILike(e.Context, $"%{search}%")
            || EF.Functions.ILike(e.Message, $"%{search}%"));
    }

    public IQueryable<Error> GetBySource(ErrorSource source) {
        return GetAll().Where(e => e.Source == source);
    }
}
