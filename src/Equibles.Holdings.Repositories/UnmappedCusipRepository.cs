using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class UnmappedCusipRepository : BaseRepository<UnmappedCusip>
{
    public UnmappedCusipRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// The identifiers costing us the most, heaviest first — the order worth working through when
    /// deciding which missing security to map next.
    /// </summary>
    public IQueryable<UnmappedCusip> GetByFiledValueDescending()
    {
        return GetAll().OrderByDescending(c => c.FiledValue);
    }
}
