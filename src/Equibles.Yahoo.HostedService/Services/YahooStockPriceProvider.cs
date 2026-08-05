using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.HostedService.Services;

public class YahooStockPriceProvider : IStockPriceProvider
{
    private const int LookbackDays = 7;

    private readonly EquiblesFinancialDbContext _dbContext;

    public YahooStockPriceProvider(EquiblesFinancialDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<
        Dictionary<(Guid CommonStockId, string ListedTicker, DateOnly Date), decimal>
    > GetClosingPrices(
        IEnumerable<(Guid CommonStockId, string ListedTicker, DateOnly Date)> requests,
        CancellationToken cancellationToken = default
    )
    {
        var result = new Dictionary<(Guid, string, DateOnly), decimal>();
        var requestList = requests.ToList();
        if (requestList.Count == 0)
            return result;

        // Group by date so we can batch-query per reporting period
        var byDate = requestList
            .GroupBy(r => r.Date)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => (r.CommonStockId, r.ListedTicker)).Distinct().ToList()
            );

        foreach (var (date, listings) in byDate)
        {
            var minDate = date.AddDays(-LookbackDays);
            var stockIds = listings.Select(l => l.CommonStockId).Distinct().ToList();
            var secondaryTickers = listings
                .Where(l => l.ListedTicker != null)
                .Select(l => l.ListedTicker)
                .Distinct()
                .ToList();

            // One window query per date: the primary series for every requested stock, plus
            // the exact secondary series requested. The precise (stock, listing) pairing is
            // re-applied in memory — the small overfetch beats a per-pair query.
            var prices = await _dbContext
                .Set<DailyStockPrice>()
                .Where(p =>
                    stockIds.Contains(p.CommonStockId)
                    && (
                        p.ListedTicker == p.CommonStock.Ticker
                        || secondaryTickers.Contains(p.ListedTicker)
                    )
                    && p.Date >= minDate
                    && p.Date <= date
                )
                .Select(p => new
                {
                    p.CommonStockId,
                    p.ListedTicker,
                    PrimaryTicker = p.CommonStock.Ticker,
                    p.Date,
                    p.Close,
                })
                .ToListAsync(cancellationToken);

            // Pick the latest price per exact series
            var latestBySeries = prices.LatestPerGroup(
                p => (p.CommonStockId, p.ListedTicker),
                p => p.Date
            );
            var byRequestedListing = latestBySeries.ToDictionary(p =>
                (
                    p.CommonStockId,
                    // The caller addresses the primary series as null; a secondary by its symbol.
                    ListedTicker: p.ListedTicker == p.PrimaryTicker ? null : p.ListedTicker
                )
            );

            foreach (var listing in listings)
            {
                if (byRequestedListing.TryGetValue(listing, out var price))
                {
                    result[(listing.CommonStockId, listing.ListedTicker, date)] = price.Close;
                }
            }
        }

        return result;
    }
}
