using Equibles.Core.AutoWiring;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsValueRecalculator {
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStockPriceProvider _stockPriceProvider;
    private readonly ILogger<HoldingsValueRecalculator> _logger;

    public HoldingsValueRecalculator(
        IServiceScopeFactory scopeFactory,
        IStockPriceProvider stockPriceProvider,
        ILogger<HoldingsValueRecalculator> logger
    ) {
        _scopeFactory = scopeFactory;
        _stockPriceProvider = stockPriceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Recalculates Value for all holdings with ValuePending = true
    /// where a Yahoo stock price is now available.
    /// </summary>
    public async Task Recalculate(CancellationToken cancellationToken) {
        // Find all distinct (stock, date) pairs that need prices
        using var lookupScope = _scopeFactory.CreateScope();
        var lookupContext = lookupScope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

        var pendingPairs = await lookupContext.Set<InstitutionalHolding>()
            .Where(h => h.ValuePending)
            .Select(h => new { h.CommonStockId, h.ReportDate })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (pendingPairs.Count == 0) {
            _logger.LogDebug("No holdings with pending values");
            return;
        }

        _logger.LogInformation("Found {Count} (stock, date) pairs with pending values", pendingPairs.Count);

        var requests = pendingPairs.Select(p => (p.CommonStockId, p.ReportDate)).ToList();
        var prices = await _stockPriceProvider.GetClosingPrices(requests, cancellationToken);

        if (prices.Count == 0) {
            _logger.LogInformation("No Yahoo prices available yet for any pending holdings");
            return;
        }

        _logger.LogInformation("Found prices for {Count}/{Total} pending pairs", prices.Count, pendingPairs.Count);

        // Process in batches per (stock, date) pair that now has a price
        var totalUpdated = 0;

        foreach (var ((stockId, reportDate), closePrice) in prices) {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

            var pendingHoldings = await dbContext.Set<InstitutionalHolding>()
                .Include(h => h.ManagerEntries)
                .Where(h => h.ValuePending && h.CommonStockId == stockId && h.ReportDate == reportDate)
                .ToListAsync(cancellationToken);

            foreach (var holding in pendingHoldings) {
                holding.Value = (long)(holding.Shares * closePrice);
                holding.ValuePending = false;

                foreach (var entry in holding.ManagerEntries) {
                    entry.Value = (long)(entry.Shares * closePrice);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            totalUpdated += pendingHoldings.Count;
        }

        _logger.LogInformation("Recalculated values for {Count} holdings", totalUpdated);
    }
}
