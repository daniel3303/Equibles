using Equibles.Congress.Data.Extensions;
using Equibles.Congress.Data.Models;
using Equibles.Data;

namespace Equibles.Congress.Repositories;

public class CongressMemberTermRepository : BaseRepository<CongressMemberTerm>
{
    public CongressMemberTermRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>The term the legislator held on <paramref name="date"/>; at most one row.</summary>
    public IQueryable<CongressMemberTerm> GetTermOn(string bioguideId, DateOnly date)
    {
        return GetAll().Where(term => term.BioguideId == bioguideId).ServingOn(date);
    }

    /// <summary>Each listed legislator's most recently started term.</summary>
    public IQueryable<CongressMemberTerm> GetLatestTerms(IReadOnlyCollection<string> bioguideIds)
    {
        return GetAll().LatestPerLegislator(bioguideIds);
    }

    /// <summary>
    /// Pairs every trade with the term its member held on the transaction date, as one left join:
    /// exactly one row per trade, with a null term when none is known.
    /// </summary>
    public IQueryable<CongressionalTradeTerm> WithTermOnTransactionDate(
        IQueryable<CongressionalTrade> trades
    )
    {
        return trades.WithTermOnTransactionDate(GetAll());
    }
}
