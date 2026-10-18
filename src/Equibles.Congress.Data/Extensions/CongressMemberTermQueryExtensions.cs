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
}
