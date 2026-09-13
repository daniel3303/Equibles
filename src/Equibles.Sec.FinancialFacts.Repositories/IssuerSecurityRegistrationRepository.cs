using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class IssuerSecurityRegistrationRepository : BaseRepository<IssuerSecurityRegistration>
{
    public IssuerSecurityRegistrationRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<IssuerSecurityRegistration> GetByIssuerId(Guid issuerId)
    {
        return GetAll().Where(s => s.EquityIssuerId == issuerId);
    }
}
