using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.Data.Extensions;

public static class InsiderTradingQueryOrderExtensions
{
    public static IOrderedQueryable<InsiderTransaction> OrderNewestFirst(
        this IQueryable<InsiderTransaction> query
    )
    {
        return query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.FilingDate)
            .ThenBy(t => t.AccessionNumber)
            .ThenBy(t => t.TransactionOrder)
            .ThenBy(t => t.Id);
    }

    public static IOrderedQueryable<Form144Filing> OrderNewestFirst(
        this IQueryable<Form144Filing> query
    )
    {
        return query
            .OrderByDescending(f => f.FilingDate)
            .ThenBy(f => f.AccessionNumber)
            .ThenBy(f => f.Id);
    }

    public static IOrderedQueryable<InsiderOwner> OrderDiscoveryMatches(
        this IQueryable<InsiderOwner> query
    )
    {
        return query
            .OrderByDescending(o =>
                o.Transactions.Max(t => (DateOnly?)t.TransactionDate) ?? DateOnly.MinValue
            )
            .ThenBy(o => o.Name)
            .ThenBy(o => o.OwnerCik)
            .ThenBy(o => o.Id);
    }
}
