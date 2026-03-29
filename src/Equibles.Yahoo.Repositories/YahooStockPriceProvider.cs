using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Repositories;

public class YahooStockPriceProvider : IStockPriceProvider {
    private const int LookbackDays = 7;

    private readonly EquiblesDbContext _dbContext;

    public YahooStockPriceProvider(EquiblesDbContext dbContext) {
        _dbContext = dbContext;
    }

    public async Task<Dictionary<(Guid CommonStockId, DateOnly Date), decimal>> GetClosingPrices(
        IEnumerable<(Guid CommonStockId, DateOnly Date)> requests,
        CancellationToken cancellationToken = default
    ) {
        var result = new Dictionary<(Guid, DateOnly), decimal>();
        var requestList = requests.ToList();
        if (requestList.Count == 0) return result;

        // Group by date so we can batch-query per reporting period
        var byDate = requestList
            .GroupBy(r => r.Date)
            .ToDictionary(g => g.Key, g => g.Select(r => r.CommonStockId).Distinct().ToList());

        foreach (var (date, stockIds) in byDate) {
            var minDate = date.AddDays(-LookbackDays);

            // Fetch candidate prices within the lookback window
            var prices = await _dbContext.Set<DailyStockPrice>()
                .Where(p => stockIds.Contains(p.CommonStockId) && p.Date >= minDate && p.Date <= date)
                .Select(p => new { p.CommonStockId, p.Date, p.Close })
                .ToListAsync(cancellationToken);

            // Pick the latest price per stock
            var latestByStock = prices
                .GroupBy(p => p.CommonStockId)
                .Select(g => g.OrderByDescending(p => p.Date).First());

            foreach (var price in latestByStock) {
                result[(price.CommonStockId, date)] = price.Close;
            }
        }

        return result;
    }
}
