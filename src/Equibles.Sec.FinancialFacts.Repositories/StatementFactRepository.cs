using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

/// <summary>
/// The fact repository of the readers that list what a company reported: it also returns the
/// figures read from a filing's statement table, which fill only lines and periods no structured
/// source states. Anything that computes from facts (ratios, valuation, matching) takes
/// <see cref="FinancialFactRepository"/>, which leaves them out.
/// </summary>
public class StatementFactRepository : FinancialFactRepository
{
    public StatementFactRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public override IQueryable<FinancialFact> GetAll()
    {
        return GetDbSet().AsQueryable();
    }
}
