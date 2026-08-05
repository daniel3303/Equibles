using Equibles.CommonStocks.Data.Models;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.IntegrationTests.Helpers;

/// <summary>
/// Adapts older test seeds to the exact-listing writer contract. Production writers must set
/// ListedTicker themselves; Yahoo writer tests opt out so they verify that requirement directly.
/// </summary>
internal sealed class DailyStockPriceSeedInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is { } context)
            PopulateFromPrimaryListings(context);

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is { } context)
            await PopulateFromPrimaryListingsAsync(context, cancellationToken);

        return result;
    }

    private static void PopulateFromPrimaryListings(DbContext context)
    {
        var pending = GetPendingPrices(context);
        if (pending.Count == 0)
            return;

        var tickers = ResolveTrackedTickers(context, pending);
        var unresolvedIds = GetUnresolvedStockIds(pending, tickers);
        if (unresolvedIds.Count > 0)
        {
            foreach (
                var stock in context
                    .Set<CommonStock>()
                    .AsNoTracking()
                    .Where(stock => unresolvedIds.Contains(stock.Id))
                    .Select(stock => new { stock.Id, stock.Ticker })
            )
            {
                tickers[stock.Id] = stock.Ticker;
            }
        }

        ApplyTickers(pending, tickers);
    }

    private static async Task PopulateFromPrimaryListingsAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        var pending = GetPendingPrices(context);
        if (pending.Count == 0)
            return;

        var tickers = ResolveTrackedTickers(context, pending);
        var unresolvedIds = GetUnresolvedStockIds(pending, tickers);
        if (unresolvedIds.Count > 0)
        {
            var stored = await context
                .Set<CommonStock>()
                .AsNoTracking()
                .Where(stock => unresolvedIds.Contains(stock.Id))
                .Select(stock => new { stock.Id, stock.Ticker })
                .ToListAsync(cancellationToken);
            foreach (var stock in stored)
                tickers[stock.Id] = stock.Ticker;
        }

        ApplyTickers(pending, tickers);
    }

    private static List<EntityEntry<DailyStockPrice>> GetPendingPrices(DbContext context) =>
        context
            .ChangeTracker.Entries<DailyStockPrice>()
            .Where(entry =>
                entry.State == EntityState.Added
                && string.IsNullOrWhiteSpace(entry.Entity.ListedTicker)
            )
            .ToList();

    private static Dictionary<Guid, string> ResolveTrackedTickers(
        DbContext context,
        IReadOnlyCollection<EntityEntry<DailyStockPrice>> pending
    )
    {
        var tickers = context
            .ChangeTracker.Entries<CommonStock>()
            .Where(entry => entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity)
            .Where(stock => !string.IsNullOrWhiteSpace(stock.Ticker))
            .GroupBy(stock => stock.Id)
            .ToDictionary(group => group.Key, group => group.Last().Ticker);

        foreach (var entry in pending)
        {
            var stock = entry.Entity.CommonStock;
            if (stock is not null && !string.IsNullOrWhiteSpace(stock.Ticker))
                tickers[stock.Id] = stock.Ticker;
        }

        return tickers;
    }

    private static HashSet<Guid> GetUnresolvedStockIds(
        IEnumerable<EntityEntry<DailyStockPrice>> pending,
        IReadOnlyDictionary<Guid, string> tickers
    ) =>
        pending
            .Select(entry => entry.Entity.CommonStockId)
            .Where(id => !tickers.ContainsKey(id))
            .ToHashSet();

    private static void ApplyTickers(
        IEnumerable<EntityEntry<DailyStockPrice>> pending,
        IReadOnlyDictionary<Guid, string> tickers
    )
    {
        foreach (var entry in pending)
        {
            if (tickers.TryGetValue(entry.Entity.CommonStockId, out var ticker))
                entry.Entity.ListedTicker = ticker;
        }
    }
}
