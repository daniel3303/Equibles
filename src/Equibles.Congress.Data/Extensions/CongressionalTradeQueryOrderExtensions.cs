using Equibles.Congress.Data.Models;

namespace Equibles.Congress.Data.Extensions;

public static class CongressionalTradeQueryOrderExtensions
{
    public static IOrderedQueryable<CongressionalTrade> OrderNewestFirst(
        this IQueryable<CongressionalTrade> query
    )
    {
        return query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.FilingDate)
            .ThenBy(t => t.Id);
    }
}
