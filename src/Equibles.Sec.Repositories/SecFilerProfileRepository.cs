using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class SecFilerProfileRepository : BaseRepository<SecFilerProfile>
{
    public SecFilerProfileRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>Profiles of canonical (zero-trimmed) CIKs.</summary>
    public IQueryable<SecFilerProfile> GetByCiks(IReadOnlyCollection<string> ciks)
    {
        return GetAll().Where(profile => ciks.Contains(profile.Cik));
    }
}
