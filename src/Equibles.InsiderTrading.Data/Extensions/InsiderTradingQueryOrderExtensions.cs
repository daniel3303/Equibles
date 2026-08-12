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

    /// <summary>
    /// The row holding an insider's CURRENT position: newest transaction day, newest
    /// filing, then the LAST row of that filing (max TransactionOrder) — a multi-row
    /// Form 4 lists a sequence of same-day transactions whose SharesOwnedAfter are
    /// intermediate balances, so only the last row is the end-of-day position (#7164,
    /// EquiblesCommercial). AccessionNumber DESC so that when one day carries several
    /// filings the later-numbered filing wins, matching the newest-filing intent.
    /// </summary>
    public static IOrderedQueryable<InsiderTransaction> OrderCurrentPositionFirst(
        this IQueryable<InsiderTransaction> query
    )
    {
        return query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.FilingDate)
            .ThenByDescending(t => t.AccessionNumber)
            .ThenByDescending(t => t.TransactionOrder)
            .ThenByDescending(t => t.Id);
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
