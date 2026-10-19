using Equibles.Congress.Data.Models;

namespace Equibles.Congress.Data.Extensions;

public static class CongressMemberTermQueryExtensions
{
    /// <summary>
    /// Terms held on <paramref name="date"/>. Terms are half-open, so on a term's end date the
    /// successor's term is returned, not this one.
    /// </summary>
    public static IQueryable<CongressMemberTerm> ServingOn(
        this IQueryable<CongressMemberTerm> query,
        DateOnly date
    ) => query.Where(term => term.StartDate <= date && date < term.EndDate);

    /// <summary>
    /// Each listed legislator's most recently started term, whose party is the legislator's
    /// current one; legislators without a term yield no row. Terms starting the same day are
    /// tie-broken by id, so each legislator yields exactly one row even if terms overlap.
    /// </summary>
    public static IQueryable<CongressMemberTerm> LatestPerLegislator(
        this IQueryable<CongressMemberTerm> terms,
        IReadOnlyCollection<string> bioguideIds
    ) =>
        terms.Where(term =>
            bioguideIds.Contains(term.BioguideId)
            && !terms.Any(later =>
                later.BioguideId == term.BioguideId
                && (
                    later.StartDate > term.StartDate
                    || (later.StartDate == term.StartDate && later.Id > term.Id)
                )
            )
        );

    /// <summary>
    /// Pairs every trade with the term its member held on the transaction date, as one left join:
    /// exactly one row per trade, with a null term when the member has no BioGuide id or no term
    /// covers the date. The term's party is the member's party on that date. Should terms ever
    /// overlap, the latest-started one (then the highest id) wins, so a trade never repeats.
    /// </summary>
    public static IQueryable<CongressionalTradeTerm> WithTermOnTransactionDate(
        this IQueryable<CongressionalTrade> trades,
        IQueryable<CongressMemberTerm> terms
    ) =>
        trades.SelectMany(
            trade =>
                terms
                    .Where(term =>
                        term.BioguideId == trade.CongressMember.BioguideId
                        && term.StartDate <= trade.TransactionDate
                        && trade.TransactionDate < term.EndDate
                    )
                    .OrderByDescending(term => term.StartDate)
                    .ThenByDescending(term => term.Id)
                    .Take(1)
                    .DefaultIfEmpty(),
            (trade, term) => new CongressionalTradeTerm { Trade = trade, Term = term }
        );
}
