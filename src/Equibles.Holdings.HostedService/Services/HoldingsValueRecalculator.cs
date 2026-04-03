using Equibles.Core.AutoWiring;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsValueRecalculator {
    private const int MaxRetries = 3;

    // Backoff schedule: retry 1 → 1 day, retry 2 → 1 week, retry 3 → 1 month
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

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
    /// where a Yahoo stock price is now available. Uses exponential backoff
    /// (1 day, 1 week, 1 month) and gives up after 3 failed retries.
    /// </summary>
    public async Task Recalculate(CancellationToken cancellationToken) {
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

        _logger.LogInformation("Found prices for {Count}/{Total} pending pairs", prices.Count, pendingPairs.Count);

        var resolvedPairKeys = prices.Keys.ToHashSet();
        var totalUpdated = 0;
        var totalGivenUp = 0;

        // Resolve holdings that now have a price
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

        // Increment retry count for unresolved pairs and give up after MaxRetries
        var unresolvedPairs = pendingPairs
            .Where(p => !resolvedPairKeys.Contains((p.CommonStockId, p.ReportDate)))
            .ToList();

        var now = DateTime.UtcNow;

        foreach (var pair in unresolvedPairs) {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

            // Only load holdings that are due for retry based on their last retry time
            var holdings = await dbContext.Set<InstitutionalHolding>()
                .Where(h => h.ValuePending && h.CommonStockId == pair.CommonStockId && h.ReportDate == pair.ReportDate)
                .ToListAsync(cancellationToken);

            var changed = false;

            foreach (var holding in holdings) {
                var delay = RetryDelays[Math.Min(holding.ValueRetryCount, MaxRetries - 1)];
                var anchor = holding.ValueLastRetryAt ?? holding.CreationTime;

                if (anchor.Add(delay) > now) continue;

                holding.ValueRetryCount++;
                holding.ValueLastRetryAt = now;

                if (holding.ValueRetryCount > MaxRetries) {
                    holding.ValuePending = false;
                    totalGivenUp++;
                }

                changed = true;
            }

            if (changed) {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation(
            "Recalculated values for {Updated} holdings, gave up on {GivenUp}",
            totalUpdated, totalGivenUp);
    }
}
