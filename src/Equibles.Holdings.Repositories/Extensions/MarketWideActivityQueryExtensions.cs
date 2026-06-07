using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories.Extensions;

// Market-wide 13F leaderboard buckets. The same four filter+order definitions are
// consumed by the holdings activity views, the CSV export, and the MCP tools, so they
// live here to keep a single source of truth for what each bucket means.
// IQueryable overloads translate to SQL for callers that page straight from the database;
// IEnumerable overloads serve callers (e.g. the CSV export) that fetch once and split the
// movers/churn sets in memory.
public static class MarketWideActivityQueryExtensions
{
    public static IQueryable<MarketWideStockActivity> TopBuyers(
        this IQueryable<MarketWideStockActivity> source
    ) =>
        source
            .Where(a => a.CurrentShares > a.PreviousShares)
            .OrderByDescending(a => a.CurrentValue - a.PreviousValue);

    public static IEnumerable<MarketWideStockActivity> TopBuyers(
        this IEnumerable<MarketWideStockActivity> source
    ) =>
        source
            .Where(a => a.CurrentShares > a.PreviousShares)
            .OrderByDescending(a => a.CurrentValue - a.PreviousValue);

    public static IQueryable<MarketWideStockActivity> TopSellers(
        this IQueryable<MarketWideStockActivity> source
    ) =>
        source
            .Where(a => a.CurrentShares < a.PreviousShares)
            .OrderBy(a => a.CurrentValue - a.PreviousValue);

    public static IEnumerable<MarketWideStockActivity> TopSellers(
        this IEnumerable<MarketWideStockActivity> source
    ) =>
        source
            .Where(a => a.CurrentShares < a.PreviousShares)
            .OrderBy(a => a.CurrentValue - a.PreviousValue);

    public static IQueryable<MarketWideStockChurn> NewPositions(
        this IQueryable<MarketWideStockChurn> source
    ) => source.Where(c => c.NewFilerCount > 0).OrderByDescending(c => c.NewFilerCount);

    public static IEnumerable<MarketWideStockChurn> NewPositions(
        this IEnumerable<MarketWideStockChurn> source
    ) => source.Where(c => c.NewFilerCount > 0).OrderByDescending(c => c.NewFilerCount);

    public static IQueryable<MarketWideStockChurn> SoldOutPositions(
        this IQueryable<MarketWideStockChurn> source
    ) => source.Where(c => c.SoldOutFilerCount > 0).OrderByDescending(c => c.SoldOutFilerCount);

    public static IEnumerable<MarketWideStockChurn> SoldOutPositions(
        this IEnumerable<MarketWideStockChurn> source
    ) => source.Where(c => c.SoldOutFilerCount > 0).OrderByDescending(c => c.SoldOutFilerCount);
}
