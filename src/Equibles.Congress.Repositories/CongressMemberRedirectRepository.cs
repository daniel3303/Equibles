using Equibles.Congress.Data.Models;
using Equibles.Data;

namespace Equibles.Congress.Repositories;

public class CongressMemberRedirectRepository : BaseRepository<CongressMemberRedirect>
{
    public CongressMemberRedirectRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// The redirect for a retired member id, if that id was merged away. Returns
    /// <see cref="IQueryable{T}"/> so the caller projects only what it needs — the portal wants
    /// the survivor's id and name to build the canonical URL, never the redirect row itself.
    /// </summary>
    public IQueryable<CongressMemberRedirect> GetByRetiredId(Guid retiredId)
    {
        return GetAll().Where(r => r.Id == retiredId);
    }
}
