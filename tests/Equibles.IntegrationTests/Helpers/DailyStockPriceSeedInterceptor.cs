using Equibles.CommonStocks.Data.Models;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.IntegrationTests.Helpers;

// Test fixtures bind prices to a native listing explicitly. Writer tests opt out so the
// production requirement to supply the source ticker is exercised without this convenience.
internal sealed class DailyStockPriceSeedInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is { } context)
            PopulateTickers(context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is { } context)
            PopulateTickers(context);
        return ValueTask.FromResult(result);
    }

    private static void PopulateTickers(DbContext context)
    {
        var pending = context
            .ChangeTracker.Entries<EquityDailyStockPrice>()
            .Where(entry =>
                entry.State == EntityState.Added
                && string.IsNullOrWhiteSpace(entry.Entity.SourceTicker)
            )
            .Select(entry => entry.Entity)
            .ToList();
        foreach (var price in pending)
            price.SourceTicker =
                price.Listing?.Ticker
                ?? context
                    .Set<EquityListing>()
                    .Where(listing => listing.Id == price.EquityListingId)
                    .Select(listing => listing.Ticker)
                    .Single();
    }
}
