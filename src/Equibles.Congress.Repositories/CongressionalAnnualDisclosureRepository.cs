using Equibles.Congress.Data.Models;
using Equibles.Data;

namespace Equibles.Congress.Repositories;

public class CongressionalAnnualDisclosureRepository : BaseRepository<CongressionalAnnualDisclosure>
{
    public CongressionalAnnualDisclosureRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CongressionalAnnualDisclosure> GetByMember(CongressMember member)
    {
        return GetAll().Where(d => d.CongressMemberId == member.Id);
    }

    public IQueryable<CongressionalAnnualDisclosure> GetByYear(int year)
    {
        return GetAll().Where(d => d.Year == year);
    }
}
